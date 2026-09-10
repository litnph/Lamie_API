using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Products.Commands;

public sealed record SetProductVisibilityCommand(int Id, bool IsVisibleOnFE) : IRequest;

public sealed class SetProductVisibilityHandler : IRequestHandler<SetProductVisibilityCommand>
{
    private readonly IProductRepository _repository;

    public SetProductVisibilityHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(SetProductVisibilityCommand request, CancellationToken cancellationToken)
    {
        var product = await _repository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("Product", request.Id);
        product.SetVisibilityOnFE(request.IsVisibleOnFE);
        await _repository.UpdateAsync(product);
    }
}
