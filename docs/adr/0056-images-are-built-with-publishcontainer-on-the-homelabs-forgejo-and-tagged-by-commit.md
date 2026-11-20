# Images are built with PublishContainer on the homelab's Forgejo and tagged by commit

**Status: Proposed.** The Kubernetes chart ([ADR 0053](0053-kubernetes-runs-the-local-topology.md)) pulls the applications' images from `image.registry`. This settles how they are built, named and tagged on Forgejo ([ADR 0055](0055-ci-runs-on-the-homelabs-forgejo-on-push-and-reports-to-github-as-commit-statuses.md), [#1044](https://github.com/congruent-it/norvia-sandbox/issues/1044), the Forgejo part of [#1016](https://github.com/congruent-it/norvia-sandbox/issues/1016)). The workflows are in [forgejo.md](../operations/forgejo.md#the-images).

## Decision

- **`dotnet publish /t:PublishContainer`, no Dockerfile.** The SDK builds the image from the project and pushes it straight to the registry, logged in with `SDK_CONTAINER_REGISTRY_UNAME` and `SDK_CONTAINER_REGISTRY_PWORD`. There is no Docker daemon in the build, no Dockerfile per application to keep in step with the project, and the seed file the TMS API carries is already part of its publish output. The image is `linux-x64` (the cluster's nodes are amd64), listens on 8080 (the chart's `appPort`) and runs as the base image's non-root user.
- **The base image is pinned by digest.** `build-image.yml` defaults `base-image` to `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:...`. The same inputs give the same image. Moving the digest is a reviewed change.
- **The NuGet cache is the runner's volume.** The restore reads the `/nuget-cache` the runner mounts in every job ([forgejo.md](../operations/forgejo.md#runner-for-ci)). There is no layer cache to keep, because a build has no Docker layers. The Web projects need Node for the Tailwind stylesheet; the runner image has it and the MSBuild target runs `npm ci`.
- **One reusable workflow.** `.forgejo/workflows/build-image.yml` (`workflow_call`: the project, the image name, the registry, whether it needs Node) builds one image and reports `forgejo/image (<name>)` to GitHub as a gate does. `.forgejo/workflows/images.yml` has one `uses:` per image. They are two files because a workflow that called itself would loop.
- **Names: `<REGISTRY>/<name>`.** `REGISTRY` is the Forgejo variable of the registry (`git.home.arpa/smirking_squiggly`, host and owner), as `runner-image.yml` uses it for `norvia-ci`, and `<name>` is the chart's image name (`tms-api`, `partner-api`, ...). `image.registry` of the chart is `REGISTRY`.
- **Tags: the short SHA, plus `latest` on `main`.** `image.tag` is the 7-character commit SHA. The chart's default `image.tag` is `latest`, so the chart does rely on it, and `main` moves a floating `latest`. A `v*` tag gives the SHA and the version tag, never `latest` (a tag may be an old commit).
- **Built on `push` to `main`, on `v*` tags and on branches named `image/*`.** Every branch would fill the registry. `workflow_dispatch` is not available on a mirror's push, so a branch asks for an image by its name. The rule is `infra/image-tags.ps1`, which has a Pester test.

## Considered Options

- **A Dockerfile per application:** familiar, and a layer cache on the runner's daemon. But a daemon per job (ADR 0055's runner) starts with no cache, and each Dockerfile repeats what the project already says (the restore, the publish, the seed file).
- **PublishContainer to the job's Docker daemon, then `docker push`:** works without registry credentials in the SDK, but needs `start-dockerd` and a second tool for no gain.
- **`latest` only:** nothing to trace a running pod to a commit.

## Consequences

- **A digest moves on purpose.** A security fix in the base image does not arrive by itself; raise the digest.
- **The SDK pushes over HTTPS and must trust `git.home.arpa`'s certificate.** The job image's certificate store decides. Not verified on the homelab; a failure there is a certificate error at the `Publish the image` step.
- **Reproducible means the same inputs, not a bit-identical digest.** The image's `created` label differs between builds.
- **Every image is one more `uses:` of `images.yml`** (the issues that follow #1044).
