using Lamie.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamie.Domain.Entities
{
    public class ProductImage : Entity
    {
        public int Id { get; private set; }
        public int ProductId { get; private set; }
        public string ImageUrl { get; private set; }
        public bool IsActive { get; private set; }
        public int SortOrder { get; private set; }

        internal ProductImage(string imageUrl, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new DomainException("Image URL is required");

            ImageUrl = imageUrl;
            SortOrder = sortOrder;
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        internal void Update(string imageUrl, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                throw new DomainException("Image URL is required");

            ImageUrl = imageUrl;
            SortOrder = sortOrder;
            IsActive = true;
        }
    }

}
