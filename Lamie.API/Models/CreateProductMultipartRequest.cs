using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Models;

public sealed class CreateProductMultipartRequest
{
    /// <summary>
    /// JSON string của CreateProductCommand
    /// </summary>
    [FromForm(Name = "payload")]
    public string Payload { get; set; } = default!;

    [FromForm(Name = "files")]
    public List<IFormFile> Files { get; set; } = new();
}

