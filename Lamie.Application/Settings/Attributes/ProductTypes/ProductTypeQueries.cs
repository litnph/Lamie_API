using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.ProductTypes;

public sealed record GetAllProductTypesQuery : IRequest<List<ProductTypeDto>>;
public sealed record GetProductTypeByIdQuery(int Id) : IRequest<ProductTypeDto>;

public sealed class GetAllProductTypesHandler : IRequestHandler<GetAllProductTypesQuery, List<ProductTypeDto>>
{
    private readonly IProductTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetAllProductTypesHandler(IProductTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<ProductTypeDto>> Handle(GetAllProductTypesQuery request, CancellationToken cancellationToken) =>
        (await _repository.GetAllAsync()).Select(_mapper.Map<ProductTypeDto>).ToList();
}

public sealed class GetProductTypeByIdHandler : IRequestHandler<GetProductTypeByIdQuery, ProductTypeDto>
{
    private readonly IProductTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetProductTypeByIdHandler(IProductTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<ProductTypeDto> Handle(GetProductTypeByIdQuery request, CancellationToken cancellationToken)
    {
        var productType = await _repository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("ProductType", request.Id);
        return _mapper.Map<ProductTypeDto>(productType);
    }
}
