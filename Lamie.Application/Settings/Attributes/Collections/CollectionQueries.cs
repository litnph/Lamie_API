using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Collections;

// Queries
public sealed record GetAllCollectionsQuery() : IRequest<List<CollectionDto>>;

public sealed record GetCollectionByIdQuery(int Id) : IRequest<CollectionDto>;

// Query Handlers
public sealed class GetAllCollectionsHandler : IRequestHandler<GetAllCollectionsQuery, List<CollectionDto>>
{
    private readonly ICollectionRepository _repository;
    private readonly IMapper _mapper;

    public GetAllCollectionsHandler(ICollectionRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CollectionDto>> Handle(GetAllCollectionsQuery request, CancellationToken cancellationToken)
    {
        var collections = await _repository.GetAllAsync();
        return collections.Select(_mapper.Map<CollectionDto>).ToList();
    }
}

public sealed class GetCollectionByIdHandler : IRequestHandler<GetCollectionByIdQuery, CollectionDto>
{
    private readonly ICollectionRepository _repository;
    private readonly IMapper _mapper;

    public GetCollectionByIdHandler(ICollectionRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<CollectionDto> Handle(GetCollectionByIdQuery request, CancellationToken cancellationToken)
    {
        var collection = await _repository.GetByIdAsync(request.Id);
        if (collection is null)
        {
            throw new NotFoundException("Collection", request.Id);
        }

        return _mapper.Map<CollectionDto>(collection);
    }
}

