variable "name" {
  description = "Server name and label prefix."
  type        = string
  default     = "tc-app-1"
}

variable "location" {
  description = "Hetzner location; the same as the streaming node, for the private network."
  type        = string
  default     = "fsn1"
}

variable "server_type" {
  description = "Hetzner server type. CX23: 2 vCPU, 4 GB RAM."
  type        = string
  default     = "cx23"
}

variable "image" {
  description = "Base OS image."
  type        = string
  default     = "debian-12"
}

variable "ssh_public_keys" {
  description = "Admin SSH public keys, keyed by name."
  type        = map(string)

  validation {
    condition     = length(var.ssh_public_keys) > 0
    error_message = "Provide at least one SSH public key."
  }
}

variable "admin_cidrs" {
  description = "CIDRs allowed to reach SSH (port 22)."
  type        = list(string)

  validation {
    condition = length(var.admin_cidrs) > 0 && alltrue([
      for cidr in var.admin_cidrs : !contains(["0.0.0.0/0", "::/0"], cidr)
    ])
    error_message = "List at least one admin CIDR, and do not open SSH to the whole internet."
  }
}

variable "network_ip_range" {
  description = "Private network shared by the control plane and the streaming nodes."
  type        = string
  default     = "10.20.0.0/16"
}

variable "subnet_ip_range" {
  description = "Subnet of the private network in the location's network zone."
  type        = string
  default     = "10.20.1.0/24"
}

variable "app_private_ip" {
  description = "Private IP of this node. Icecast calls source auth at http://<this>:8081."
  type        = string
  default     = "10.20.1.10"
}

variable "streaming_nodes" {
  description = "Existing streaming servers (tc-streaming Terraform) to attach to the private network: name => private IP."
  type        = map(string)
  default     = { "tc-stream-1" = "10.20.1.2" }
}

variable "cloudflare_zone_id" {
  description = "Cloudflare zone ID of the domain. Null skips the DNS records."
  type        = string
  default     = null
}

variable "app_hostname" {
  description = "Full DNS name of the web app and API."
  type        = string
  default     = "app.tropicastradio.com"
}
