using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.MasterData.Tags;

// Queries
public sealed record GetAllTagsQuery() : IRequest<List<TagDto>>;

public sealed record GetTagByIdQuery(int Id) : IRequest<TagDto>;

// Query Handlers
public sealed class GetAllTagsHandler : IRequestHandler<GetAllTagsQuery, List<TagDto>>
{
    private readonly ITagRepository _repository;
    private readonly IMapper _mapper;

    public GetAllTagsHandler(ITagRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagDto>> Handle(GetAllTagsQuery request, CancellationToken cancellationToken)
    {
        var tags = await _repository.GetAllAsync();
        return tags.Select(_mapper.Map<TagDto>).ToList();
    }
}

public sealed class GetTagByIdHandler : IRequestHandler<GetTagByIdQuery, TagDto>
{
    private readonly ITagRepository _repository;
    private readonly IMapper _mapper;

    public GetTagByIdHandler(ITagRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<TagDto> Handle(GetTagByIdQuery request, CancellationToken cancellationToken)
    {
        var tag = await _repository.GetByIdAsync(request.Id);
        if (tag is null)
        {
            throw new NotFoundException("Tag", request.Id);
        }

        return _mapper.Map<TagDto>(tag);
    }
}

