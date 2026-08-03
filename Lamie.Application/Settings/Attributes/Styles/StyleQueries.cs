using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Styles;

// Queries
public sealed record GetAllStylesQuery() : IRequest<List<StyleDto>>;

public sealed record GetStyleByIdQuery(int Id) : IRequest<StyleDto>;

// Query Handlers
public sealed class GetAllStylesHandler : IRequestHandler<GetAllStylesQuery, List<StyleDto>>
{
    private readonly IStyleRepository _repository;
    private readonly IMapper _mapper;

    public GetAllStylesHandler(IStyleRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<StyleDto>> Handle(GetAllStylesQuery request, CancellationToken cancellationToken)
    {
        var styles = await _repository.GetAllAsync();
        return styles.Select(_mapper.Map<StyleDto>).ToList();
    }
}

public sealed class GetStyleByIdHandler : IRequestHandler<GetStyleByIdQuery, StyleDto>
{
    private readonly IStyleRepository _repository;
    private readonly IMapper _mapper;

    public GetStyleByIdHandler(IStyleRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<StyleDto> Handle(GetStyleByIdQuery request, CancellationToken cancellationToken)
    {
        var style = await _repository.GetByIdAsync(request.Id);
        if (style is null)
        {
            throw new NotFoundException("Style", request.Id);
        }

        return _mapper.Map<StyleDto>(style);
    }
}

