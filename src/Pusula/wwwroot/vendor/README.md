# Vendored libraries

Third-party code, copied unmodified from the npm registry. pusula works without internet access,
so nothing is loaded from a CDN. Each library lists where it came from and how the download was
checked; `tests/web/static.test.mjs` fails if a bundle no longer matches the hash recorded here.

## markdown-it

| | |
|---|---|
| Package | `markdown-it` |
| Version | 15.0.2 (npm `latest` on 2026-10-01) |
| License | MIT, see `markdown-it/LICENSE` |
| Source | https://registry.npmjs.org/markdown-it/-/markdown-it-15.0.2.tgz |
| Downloaded | 2026-10-01 |
| Tarball integrity | `sha512-q4IGxMv56jCqT4OCRCADBoDP3LO4MhmTXjFbphHPXs4g3j9Xg5RDnxqN8IF/3vIWEU+VCnUq+7JUg/cfy2E6Qw==` |
| Integrity check | The registry's `dist.integrity` (from `npm view markdown-it@15.0.2 dist.integrity`) equals the sha512 of the downloaded tarball: verified 2026-10-01 |
| Tarball sha256 | `3237f5ecac432f99453e6d8d345b6e4764a22f1781c96e02dc9e89777b9c23c1` |
| Bundle | `package/dist/browser/markdown-it.umd.min.js` saved as `markdown-it/markdown-it.umd.min.js` (115,080 bytes) |
| Bundle sha256 | `635972b985228e8af9f0143647c68616b7a3bb09f6946e7e4a52e43dcf5e7be5` |
| License file | `package/LICENSE` saved as `markdown-it/LICENSE` |

Notes:

- The bundle is UMD and sets `window.markdownit`. `index.html` loads it with a classic `<script>`
  before the `js/app.js` module. It needs no `eval`, so it runs under the page's CSP.
- The source map (`markdown-it.umd.min.js.map`) is not included, so the file stays byte-identical
  to the package. Browser dev tools may report the missing map; nothing else is affected.
- Settings used by the page: `html: false`, `linkify: false`, `typographer: false` (see
  `createMarkdown` in `js/core.js`).

To check or update the download:

```sh
curl -sSfLO https://registry.npmjs.org/markdown-it/-/markdown-it-15.0.2.tgz
echo "sha512-$(openssl dgst -sha512 -binary markdown-it-15.0.2.tgz | openssl base64 -A)"
npm view markdown-it@15.0.2 dist.integrity
```

The two values must be identical. Then extract `package/dist/browser/markdown-it.umd.min.js` and
`package/LICENSE`, and update the version, hashes and date above.
