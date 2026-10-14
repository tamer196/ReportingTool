# syntax=docker/dockerfile:1
# Build context: src/Web
#   docker build -t reporting-tool-web src/Web

FROM node:25-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

# Non-root nginx listening on 8080. The template is rendered at container start with
# API_UPSTREAM (the API's address inside the network), so one image serves every environment.
FROM nginxinc/nginx-unprivileged:stable-alpine AS runtime
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/reporting-web/browser /usr/share/nginx/html
# The image's own non-root user, stated so scanners (and readers) can see it.
USER 101
EXPOSE 8080
