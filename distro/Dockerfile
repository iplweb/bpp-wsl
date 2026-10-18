FROM debian:trixie

ARG BPP_IMAGE_TAG
RUN test -n "$BPP_IMAGE_TAG" || { echo "BPP_IMAGE_TAG jest wymagany" >&2; exit 1; }

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      systemd systemd-sysv ca-certificates curl jq openssl util-linux iproute2 \
 && install -m 0755 -d /etc/apt/keyrings \
 && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
 && chmod a+r /etc/apt/keyrings/docker.asc \
 && . /etc/os-release \
 && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian ${VERSION_CODENAME} stable" \
      > /etc/apt/sources.list.d/docker.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends \
      docker-ce docker-ce-cli containerd.io docker-compose-plugin \
 && systemctl enable docker \
 && apt-get clean \
 && rm -rf /var/lib/apt/lists/*

COPY rootfs/ /
RUN chmod 0755 /opt/bpp/bin/* \
 && chmod a+r /opt/bpp/lib/*.sh \
 && echo "$BPP_IMAGE_TAG" > /opt/bpp/image-tag \
 && ln -s /opt/bpp/bin/bpp-ctl /usr/local/bin/bpp-ctl \
 && : > /etc/machine-id \
 && rm -f /var/lib/dbus/machine-id
