# BorisRay for Windows

<!-- impeccable:product-schema 1 -->

## Platform

Windows native desktop; the skill's platform vocabulary has no Windows value.

## Stack

The owner previously authorized another client base. This port uses native C# Windows Forms and the same pinned sing-box 1.14.3 protocol core, with a self-contained Windows x64 distribution. The Windows toolchain is an implementation choice under that existing delegation.

## Users

People using their own VPN subscriptions on Windows 11 Intel/AMD x64. The owner explicitly wants to distribute the Windows client for free. This revision is Windows only; existing Mac and Android clients are outside scope.

## Product Purpose

Import a user-provided VPN subscription, connect and choose application/website routes, and inspect actual connections. The software is free; it does not supply a VPN service or server access.

## Capabilities and Constraints

Real connect/disconnect, VLESS TCP/TLS/Reality, VMess, Trojan, Shadowsocks AEAD/2022, Hysteria2, TUIC and SOCKS5; HTTPS, plain/Base64, Xray/sing-box JSON and Clash YAML server import; standard Windows installer, country/server selection from explicit subscription labels, application and domain rules, connection monitoring, bounded speed/upload/HTTPS latency. Website rules precede application rules. An application shortcut must resolve to its actual executable. Device credentials stay private and outside distributable files. No family administration or bundled provider credential. No provider-specific default routes. Existing Mac VPN and server routes must remain untouched. The Windows tunnel needs administrator consent; compilation/config checks are distinct from Windows kernel routing acceptance.

## Brand Commitments

Inherit the approved English gold BorisRay wordmark, original golden manta, dark utility interface and existing desktop navigation. This is a platform port of the confirmed Mac composition, not a new identity or concept decision. Previous human answers in the parent project settle the product/brand/platform questions.

## Evidence on Hand

Canonical Mac PRODUCT.md, DESIGN.md, Sources/App.swift and Assets/boris-ray-icon.png under ../2026-10-17/lichny-borisa-split; Android's strict subscription parser and physical-network DNS strategy. Windows source is cross-compiled on this Mac and built/tested on the standard GitHub Windows runner, including DPAPI, protected engine staging, shortcut resolution and native Forms bitmap captures. Physical Windows 11 TUN acceptance is still outstanding. No family secrets are embedded.
