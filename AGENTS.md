# Xiaomang source maintenance

- Preserve user settings and learned input vocabulary during upgrades.
- Build desktop releases with scripts/Build.ps1 -Component Desktop -Publish.
- Never commit personal data, credentials, build output or development backups.
- For a GitHub release, publish matching source, usage package, version metadata and SHA256 checksums.
- Check on-demand components for distributable changes; publish changed components and their corresponding source, update channel and main-app references together. Validate upgrade preservation of settings and learned vocabulary.
- Stop input workers only through the product pause/resume lifecycle; preserve the prior paused state.
