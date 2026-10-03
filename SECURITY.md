# Security policy

## Supported versions

Only the latest release is supported. A security fix is released as a new version; older versions are not patched.
pusula is updated with `git pull` (see *Updating* in the [README](README.md#updating)), so staying current takes one command.

## Reporting a vulnerability

**Please do not report a vulnerability in a public issue, discussion or pull request.**

Use GitHub's private vulnerability reporting: open the **Security** tab of the repository, choose **Report a
vulnerability** and fill in the form (<https://github.com/faraday208/pusula/security/advisories/new>). Only you and the
maintainer can see the report.

If that button is not there, open an ordinary issue that says only that you have a security report, with no details,
and a private way to send it will be arranged.

Please include:

- what is affected: the pusula version, and how you started it (the `--urls` value, whether `Pusula:AllowRemoteEdit` was set);
- the steps that reproduce it, using a small made-up folder, not your own notes or configuration;
- what an attacker could read, write or run, and from where (the same computer, a private network, another website);
- a fix or a mitigation, if you have one in mind.

## What to expect

pusula has a single maintainer and is looked after on a best-effort basis: there is no guaranteed response time.
Reports are read in the order they arrive, serious ones first. A fix is released as a new version and noted in
[CHANGELOG.md](CHANGELOG.md). Please give it a reasonable time before you publish the details.

## Scope

pusula is a local tool: it runs on your own computer and only reads. Its threat model is in the README, under
[Remote access](README.md#remote-access) and [Privacy and safety](README.md#privacy-and-safety): it has no login by
design, a host guard limits which names the server answers to, it never writes to the folders it shows, and it serves
only `.md` files.

- **In scope:** a way around one of those protections, for example reading a file that is not an indexed `.md` file,
  writing to a folder it shows, changing sources or browsing folders from another website (or from another machine
  without `Pusula:AllowRemoteEdit`), getting past the host guard, or running script through rendered Markdown.
- **Out of scope:** running pusula open to the internet or on a network you do not trust (the README says not to),
  someone who already has access to the computer or to your private network, and a weakness in a dependency that does
  not affect pusula (report that upstream).
