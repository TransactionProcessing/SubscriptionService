using System.Text;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Run(async context =>
{
    var request = context.Request;
    request.EnableBuffering();

    string body;
    using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
    {
        body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
    }

    var timestamp = DateTimeOffset.UtcNow.ToString("O");
    Console.WriteLine($"[{timestamp}] {request.Method} {request.Scheme}://{request.Host}{request.Path}{request.QueryString}");

    foreach (var header in request.Headers)
    {
        Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value.Select(value => value ?? string.Empty))}");
    }

    if (!string.IsNullOrWhiteSpace(body))
    {
        Console.WriteLine();
        Console.WriteLine(body);
    }

    Console.WriteLine(new string('-', 80));

    context.Response.StatusCode = StatusCodes.Status204NoContent;
});

app.Run();
