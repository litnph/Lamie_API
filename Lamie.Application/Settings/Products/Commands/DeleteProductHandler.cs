using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Storage;
using Lamie.Domain.Repositories;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Lamie.Application.Settings.Products.Commands
{
    public class DeleteProductHandler : IRequestHandler<DeleteProductCommand>
    {
        private readonly IProductRepository _repository;
        private readonly IFileStorage _fileStorage;

        public DeleteProductHandler(IProductRepository repository, IFileStorage fileStorage)
        {
            _repository = repository;
            _fileStorage = fileStorage;
        }

        public async Task Handle(DeleteProductCommand request, CancellationToken cancellationToken)
        {
            var product = await _repository.GetByIdAsync(request.Id);
            if (product is null)
            {
                throw new NotFoundException("Product", request.Id);
            }

            if (await _repository.HasOrderReferencesAsync(request.Id, cancellationToken))
            {
                throw new ConflictException("Product is referenced by order history and cannot be deleted.");
            }

            var storedUrls = product.Images
                .Select(image => image.ImageUrl)
                .Append(product.ThumbnailUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await _repository.DeleteAsync(product);

            foreach (var storedUrl in storedUrls)
            {
                await _fileStorage.DeleteAsync(storedUrl, cancellationToken);
            }
        }
    }
}

