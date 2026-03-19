# CLI Tools

The `cocoar-secrets` CLI tool encrypts values and manages certificates from the command line.

::: info Installation
```shell
dotnet tool install -g Cocoar.Configuration.Secrets.Cli
```
:::

## Encrypting a Value

```shell
cocoar-secrets encrypt \
    --value "Server=prod;Password=s3cret" \
    --cert certs/prod.pfx \
    --kid prod-secrets
```

The output is a JSON envelope you paste into your config file:

```json
{
  "type": "cocoar.secret",
  "version": 1,
  "kid": "prod-secrets",
  "alg": "RSA-OAEP-AES256-GCM",
  "wk": "...",
  "iv": "...",
  "ct": "...",
  "tag": "..."
}
```

### Encrypt from stdin

Pipe values to avoid them appearing in shell history:

```shell
echo -n "s3cret" | cocoar-secrets encrypt --cert certs/prod.pfx --kid prod-secrets
```

## Generating a Certificate

```shell
cocoar-secrets generate-cert -o certs/prod.pfx
```

Generates a self-signed X.509 certificate suitable for secret encryption. The output is a password-less PFX file.

## Converting Certificates

Convert password-protected certificates to password-less format:

```shell
cocoar-secrets convert-cert \
    --input certs/protected.pfx \
    --output certs/prod.pfx
```

The library requires password-less certificates — protection is handled by file system ACLs, not passwords embedded in the certificate file.

## Decrypting a Value

For debugging or migration:

```shell
cocoar-secrets decrypt \
    --value '{"type":"cocoar.secret",...}' \
    --cert certs/prod.pfx
```

Or from a file:

```shell
cocoar-secrets decrypt --file appsettings.json --path "Database:Password" --cert certs/prod.pfx
```
