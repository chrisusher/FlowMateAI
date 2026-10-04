FROM quay.io/keycloak/keycloak:26.6.2@sha256:3e58dc82a9b72f364f37dac17f99fcaf53d0052a3c1ad7f00b069a1ed43201c0

COPY realm-import/ /opt/keycloak/data/import/
RUN /opt/keycloak/bin/kc.sh build --db=postgres --health-enabled=true --metrics-enabled=true

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
