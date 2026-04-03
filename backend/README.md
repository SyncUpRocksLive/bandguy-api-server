export BUILD_VERSION=0.1.1

docker build \
    --build-arg BUILD_VERSION=${BUILD_VERSION} \
    -t ghcr.io/syncuprockslive/bandguy-api-server:v${BUILD_VERSION} \
    -t ghcr.io/syncuprockslive/bandguy-api-server:latest \
    -f Dockerfile .

docker run \
    -e ConnectionStrings__WebApiDatabase="Host=host.docker.internal;Database=webapi;Username=myuser;Password=mypassword" \
    -e ConnectionStrings__BandguyDatabase="Host=host.docker.internal;Database=bandguy;Username=myuser;Password=mypassword" \
    -e Authentication__OpenIdConnectOptions__ClientId=bandguy \
    --rm -p 4666:9001 ghcr.io/syncuprockslive/bandguy-api-server:latest

export CR_PAT=YOUR_TOKEN
echo $CR_PAT | docker login ghcr.io -u USERNAME --password-stdin

docker push ghcr.io/syncuprockslive/bandguy-api-server --all-tags

