# Lesson 24 - Capstone Deploy & Review
# Builds and packages both the Responses host and its companion MCP server into a single image,
# since the host launches the MCP server as a subprocess over stdio rather than calling it over a
# network - both need to be inside the same container. Standard multi-stage .NET container
# pattern; the parts specific to this lesson are the CAPSTONE_MCP_SERVER_PATH and
# CAPSTONE_LISTEN_URL environment variables set at the bottom.
#
# Build from this lesson's src folder (the parent of both project folders):
#   docker build -f CapstoneDeployAndReview/Dockerfile -t capstone-pipeline .
# Run:
#   docker run --rm -p 5200:5200 -e OPENAI_API_KEY=$env:OPENAI_API_KEY capstone-pipeline

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CapstoneDeployAndReview/CapstoneDeployAndReview.csproj CapstoneDeployAndReview/
COPY ResearchMcpServer/ResearchMcpServer.csproj ResearchMcpServer/
RUN dotnet restore CapstoneDeployAndReview/CapstoneDeployAndReview.csproj
RUN dotnet restore ResearchMcpServer/ResearchMcpServer.csproj

COPY CapstoneDeployAndReview/ CapstoneDeployAndReview/
COPY ResearchMcpServer/ ResearchMcpServer/

RUN dotnet publish CapstoneDeployAndReview/CapstoneDeployAndReview.csproj -c Release -o /app/host
RUN dotnet publish ResearchMcpServer/ResearchMcpServer.csproj -c Release -o /app/mcp-server

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/host ./host
COPY --from=build /app/mcp-server ./mcp-server

# The host resolves the MCP server via this override instead of its dev-time
# AppContext.BaseDirectory-relative sibling-project lookup, which does not apply once both
# projects' build outputs sit side by side inside one image rather than in a source checkout.
ENV CAPSTONE_MCP_SERVER_PATH=/app/mcp-server/ResearchMcpServer.dll

# Bind all interfaces, not just localhost - "http://localhost:5200" is unreachable from outside
# the container even with the port published via -p.
ENV CAPSTONE_LISTEN_URL=http://+:5200

EXPOSE 5200

ENTRYPOINT ["dotnet", "host/CapstoneDeployAndReview.dll"]
