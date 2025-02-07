using Api.Services;
using SoapCore;
using ModelLibrary;

var builder = WebApplication.CreateBuilder(args);

// ✅ Correct order: Register SoapCore before controllers
builder.Services.AddSoapCore();
builder.Services.AddSingleton<ICalculatorService, CalculatorService>();
builder.Services.AddSingleton<IUserService, UserService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi(); // OpenAPI (Swagger)

var app = builder.Build();

// ✅ Ensure the request pipeline is properly configured
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRouting(); // ✅ Ensure routing is enabled

// ✅ Correct way to expose SOAP endpoint
app.UseSoapEndpoint<ICalculatorService>("/CalculatorService.asmx", new SoapEncoderOptions());
app.UseSoapEndpoint<IUserService>("/UserService.asmx", new SoapEncoderOptions()).UseAuthentication();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();