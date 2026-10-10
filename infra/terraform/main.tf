locals {
  labels = {
    project = "tropicast"
    role    = "control-plane"
    node    = var.name
  }
}

resource "hcloud_ssh_key" "admin" {
  for_each   = var.ssh_public_keys
  name       = "${var.name}-${each.key}"
  public_key = each.value
  labels     = local.labels
}

# Primary IPs outlive the server, so a rebuilt node keeps its addresses and
# DNS does not change.
resource "hcloud_primary_ip" "ipv4" {
  name              = "${var.name}-ipv4"
  type              = "ipv4"
  location          = var.location
  auto_delete       = false
  delete_protection = true
  labels            = local.labels
}

resource "hcloud_primary_ip" "ipv6" {
  name              = "${var.name}-ipv6"
  type              = "ipv6"
  location          = var.location
  auto_delete       = false
  delete_protection = true
  labels            = local.labels
}

# Public side: SSH from admins, HTTP(S) for Caddy. Hetzner firewalls do not
# filter the private network: source auth (8081) is bound to the private IP
# only, in deploy/compose.yaml.
resource "hcloud_firewall" "app" {
  name   = "${var.name}-fw"
  labels = local.labels

  # CI deploys open SSH for their own runner per run (deploy/ci-ssh-access.sh).
  rule {
    description = "SSH from admins"
    direction   = "in"
    protocol    = "tcp"
    port        = "22"
    source_ips  = var.admin_cidrs
  }

  rule {
    description = "HTTP (ACME and redirect to HTTPS)"
    direction   = "in"
    protocol    = "tcp"
    port        = "80"
    source_ips  = ["0.0.0.0/0", "::/0"]
  }

  rule {
    description = "HTTPS"
    direction   = "in"
    protocol    = "tcp"
    port        = "443"
    source_ips  = ["0.0.0.0/0", "::/0"]
  }

  rule {
    description = "HTTP/3"
    direction   = "in"
    protocol    = "udp"
    port        = "443"
    source_ips  = ["0.0.0.0/0", "::/0"]
  }

  rule {
    description = "ICMP for path MTU discovery and diagnostics"
    direction   = "in"
    protocol    = "icmp"
    source_ips  = ["0.0.0.0/0", "::/0"]
  }
}

# Control plane <-> streaming nodes: source auth (node -> app:8081) and
# station-limit provisioning (app -> node:22, forced command).
resource "hcloud_network" "private" {
  name     = "tropicast-private"
  ip_range = var.network_ip_range
  labels   = local.labels
}

resource "hcloud_network_subnet" "private" {
  network_id   = hcloud_network.private.id
  type         = "cloud"
  network_zone = "eu-central"
  ip_range     = var.subnet_ip_range
}

resource "hcloud_server" "app" {
  name               = var.name
  server_type        = var.server_type
  image              = var.image
  location           = var.location
  ssh_keys           = [for key in hcloud_ssh_key.admin : key.id]
  firewall_ids       = [hcloud_firewall.app.id]
  delete_protection  = true
  rebuild_protection = true
  labels             = local.labels
  user_data          = file("${path.module}/cloud-init.yaml")

  public_net {
    ipv4_enabled = true
    ipv4         = hcloud_primary_ip.ipv4.id
    ipv6_enabled = true
    ipv6         = hcloud_primary_ip.ipv6.id
  }

  network {
    network_id = hcloud_network.private.id
    ip         = var.app_private_ip
  }

  # Host setup is owned by Ansible; do not rebuild the node when the base
  # image or bootstrap file changes.
  lifecycle {
    ignore_changes = [image, user_data, ssh_keys]
  }

  depends_on = [hcloud_network_subnet.private]
}

# The streaming nodes belong to tc-streaming's Terraform; only their network
# attachment lives here. Attaching does not restart them.
data "hcloud_server" "streaming" {
  for_each = var.streaming_nodes
  name     = each.key
}

resource "hcloud_server_network" "streaming" {
  for_each   = var.streaming_nodes
  server_id  = data.hcloud_server.streaming[each.key].id
  network_id = hcloud_network.private.id
  ip         = each.value

  depends_on = [hcloud_network_subnet.private]
}

# DNS only (not proxied): Caddy gets its own certificate, and the desktop
# app's long polls and the API need no proxy.
locals {
  dns_records = var.cloudflare_zone_id == null ? {} : {
    A    = hcloud_primary_ip.ipv4.ip_address
    AAAA = hcloud_server.app.ipv6_address
  }
}

resource "cloudflare_dns_record" "app" {
  for_each = local.dns_records
  zone_id  = var.cloudflare_zone_id
  name     = var.app_hostname
  type     = each.key
  content  = each.value
  ttl      = 300
  proxied  = false
  comment  = "tc-dashboard (Terraform)"
}
