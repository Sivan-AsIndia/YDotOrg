# ---------- Stage 1: build the Angular bundle ----------
FROM node:24-alpine AS build

WORKDIR /app

# Copy manifests first so the dependency layer stays cached across source edits.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# "production" swaps in environment.prod.ts, whose apiBaseUrl is the relative
# /api/v1 that nginx proxies to the IAM API.
ARG BUILD_CONFIGURATION=production
RUN npm run build -- --configuration $BUILD_CONFIGURATION

# ---------- Stage 2: serve the static bundle ----------
FROM nginx:1.27-alpine AS runtime

RUN rm -rf /usr/share/nginx/html/*

# @angular/build:application emits the browser bundle under dist/<project>/browser
COPY --from=build /app/dist/YDot/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget -q --spider http://127.0.0.1/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
