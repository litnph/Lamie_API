using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Application.Settings.Products.Dtos
{
    public class ProductDto
    {
        public int Id { get; private set; }
        public string Sku { get; private set; } = string.Empty;
        public decimal Price { get; private set; }
        public decimal? SalePrice { get; private set; }
        public int Stock { get; private set; }
        public bool TracksInventory { get; private set; }
        public bool IsActive { get; private set; }
    }
}
