using BLL.Services;
using DAL;
using DAL.Contracts;
using DAL.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(connectionString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// DI
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();