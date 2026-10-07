var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("collabdb");

var api = builder
    .AddProject<Projects.CollaborativeEditing_Server>("server")
    .WithReference(postgres)
    .WaitFor(postgres);

builder
    .AddProject<Projects.CollaborativeEditing_Client_Blazor>("blazor")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
