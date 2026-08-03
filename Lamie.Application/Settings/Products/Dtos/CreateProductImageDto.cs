using Microsoft.AspNetCore.Http;

namespace Lamie.Application.Settings.Products.Dtos
{
    public class CreateProductImageDto
    {
        public int? Id { get; set; }
        public string? ImageUrl { get; set; } = default!;
        public int? SortOrder { get; set; }
        public IFormFile? ImageFile { get; set; }
    }
}
