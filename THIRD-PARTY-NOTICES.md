# Third-party software

MultiCloudflared is an independent Windows supervisor, not a Cloudflare product.

The official cloudflared engine is downloaded unmodified from https://github.com/cloudflare/cloudflared/releases, and is licensed under Apache-2.0. It is not included in this project's ZIP. Its source and license: https://github.com/cloudflare/cloudflared/blob/master/LICENSE

The standalone executable includes the Microsoft .NET runtime and libraries (MIT license), including System.ServiceProcess.ServiceController and System.Security.Cryptography.ProtectedData.
- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

Copies of the .NET license and third-party notices are included in the release package.
Cloudflare and Windows trademarks belong to their respective owners.
