FROM debian:trixie-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends bats jq openssl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
# Docker CLI + compose plugin (socket hosta montowany przez build/test-distro.sh)
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
    && echo "deb [signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian trixie stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*
