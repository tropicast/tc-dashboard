output "server_id" {
  value = hcloud_server.app.id
}

output "ipv4" {
  value = hcloud_primary_ip.ipv4.ip_address
}

# The server's IPv6 address (::1 of its /64), for AAAA records.
output "ipv6" {
  value = hcloud_server.app.ipv6_address
}

output "private_ip" {
  description = "Source-auth address for tc-streaming: ICECAST_SOURCE_AUTH_URL=http://<this>:8081/internal/icecast/source-auth"
  value       = var.app_private_ip
}

output "streaming_private_ips" {
  description = "Provisioning__Ssh__Host for each streaming node."
  value       = var.streaming_nodes
}

output "hostname" {
  value = var.app_hostname
}
