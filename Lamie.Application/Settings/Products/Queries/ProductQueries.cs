using MediatR;
using Lamie.Application.Settings.Products.Dtos;

namespace Lamie.Application.Settings.Products.Queries
{
    public class GetAllProductsQuery : IRequest<List<ProductDetailsDto>>
    {
    }

    public class GetProductByIdQuery : IRequest<ProductDetailsDto>
    {
        public int Id { get; set; }

        public GetProductByIdQuery(int id)
        {
            Id = id;
        }
    }
}
