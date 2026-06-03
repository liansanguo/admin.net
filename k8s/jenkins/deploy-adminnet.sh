#!/usr/bin/env bash
set -Eeuo pipefail

BUILD_NUMBER="${1:-manual}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

API_IMAGE="adminnet-api:k8s"
WEB_IMAGE="adminnet-web:k8s"
API_TAR="/root/adminnet-api-k8s.tar"
WEB_TAR="/root/adminnet-web-k8s.tar"
NODES=("192.168.1.20" "192.168.1.30")

echo "Deploy Admin.NET build ${BUILD_NUMBER}"
echo "Workspace: ${ROOT_DIR}"

cd "${ROOT_DIR}"

echo "Clean old build outputs"
rm -rf publish/api Web/dist
mkdir -p publish/api

echo "Build API with .NET SDK container"
docker run --rm \
  -v "${ROOT_DIR}:/src" \
  -w /src/Admin.NET \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -lc "dotnet restore Admin.NET.sln && dotnet publish Admin.NET.Web.Entry/Admin.NET.Web.Entry.csproj -c Release -r linux-x64 --self-contained true -o /src/publish/api /p:PublishSingleFile=false"

echo "Build Web with Node container"
docker run --rm \
  -v "${ROOT_DIR}:/src" \
  -w /src/Web \
  node:22-alpine \
  sh -lc "corepack enable && corepack prepare pnpm@10.28.2 --activate && pnpm install --frozen-lockfile && pnpm build"

echo "Build Docker images"
docker build -f Admin.NET/Dockerfile.k8s -t "${API_IMAGE}" .
docker build -f Web/Dockerfile.k8s -t "${WEB_IMAGE}" .

echo "Save images"
docker save "${API_IMAGE}" -o "${API_TAR}"
docker save "${WEB_IMAGE}" -o "${WEB_TAR}"

echo "Distribute images to worker nodes"
for node in "${NODES[@]}"; do
  scp -o StrictHostKeyChecking=no "${API_TAR}" "${WEB_TAR}" "root@${node}:/root/"
  ssh -o StrictHostKeyChecking=no "root@${node}" "docker load -i ${API_TAR} && docker load -i ${WEB_TAR}"
done

echo "Apply Kubernetes manifests"
kubectl apply -f k8s/adminnet.yaml
kubectl rollout restart deployment/adminnet-api -n admin-net
kubectl rollout restart deployment/adminnet-web -n admin-net
kubectl rollout status deployment/adminnet-api -n admin-net --timeout=300s
kubectl rollout status deployment/adminnet-web -n admin-net --timeout=300s

echo "Deployment result"
kubectl get pods,svc,ingress -n admin-net -o wide
