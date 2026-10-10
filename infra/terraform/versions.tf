terraform {
  required_version = ">= 1.10"

  required_providers {
    hcloud = {
      source  = "hetznercloud/hcloud"
      version = "~> 1.54"
    }
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 5.0"
    }
  }

  # Remote state settings come from backend.hcl; see backend.hcl.example.
  # Same bucket as tc-streaming, another key.
  backend "s3" {}
}

# Reads the API token from the HCLOUD_TOKEN environment variable.
provider "hcloud" {}

# Reads CLOUDFLARE_API_TOKEN: a token limited to "Zone DNS: Edit" on the zone.
provider "cloudflare" {}
