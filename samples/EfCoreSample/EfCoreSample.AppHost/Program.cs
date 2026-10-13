var builder = DistributedApplication.CreateBuilder(args);
var server = builder
    .AddProject<Projects.EfCoreSample_Server>("server")
    .WithHttpEndpoint(port: 5000, isProxied: false)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");
builder
    .AddProject<Projects.EfCoreSample_Client>("client")
    .WithReference(server)
    .WaitFor(server)
    .WithExplicitStart();
builder.Build().Run();
