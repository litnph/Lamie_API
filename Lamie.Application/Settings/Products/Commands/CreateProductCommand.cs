using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Lamie.Application.Settings.Products.Dtos;
using Microsoft.AspNetCore.Http;

namespace Lamie.Application.Settings.Products.Commands
{
    public class CreateProductCommand : IRequest<int>
    {
        public string Sku { get; set; } = default!;
        public decimal Price { get; set; }
        public decimal? SalePrice { get; set; }
        public int Stock { get; set; }
        public bool TracksInventory { get; set; }
        public int CategoryId { get; set; }
        public int ProductTypeId { get; set; }
        public IFormFile? ThumbnailFile { get; set; }
        public string? ThumbnailUrl { get; set; }

        public List<int> TagIds { get; set; } = new();
        public List<int> ColorIds { get; set; } = new();
        public List<int> CollectionIds { get; set; } = new();
        public List<int> StyleIds { get; set; } = new();
        public List<int> OccasionIds { get; set; } = new();

        public List<CreateProductTranslationDto> Translations { get; set; } = new();
        public List<CreateProductImageDto> Images { get; set; } = new();
    }
}
