var builder = DistributedApplication.CreateBuilder(args);
var server = builder
    .AddProject<Projects.EfCoreSample_Server>("server")
    .WithHttpEndpoint(targetPort: 5000)
    .WithHttpHealthCheck("/health");
builder.AddProject<Projects.EfCoreSample_Client>("client").WithReference(server).WaitFor(server);
builder.Build().Run();
