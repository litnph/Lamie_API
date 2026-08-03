using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Colors;

// Queries
public sealed record GetAllColorsQuery() : IRequest<List<ColorDto>>;

public sealed record GetColorByIdQuery(int Id) : IRequest<ColorDto>;

// Query Handlers
public sealed class GetAllColorsHandler : IRequestHandler<GetAllColorsQuery, List<ColorDto>>
{
    private readonly IColorRepository _repository;
    private readonly IMapper _mapper;

    public GetAllColorsHandler(IColorRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<ColorDto>> Handle(GetAllColorsQuery request, CancellationToken cancellationToken)
    {
        var colors = await _repository.GetAllAsync();
        return colors.Select(_mapper.Map<ColorDto>).ToList();
    }
}

public sealed class GetColorByIdHandler : IRequestHandler<GetColorByIdQuery, ColorDto>
{
    private readonly IColorRepository _repository;
    private readonly IMapper _mapper;

    public GetColorByIdHandler(IColorRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<ColorDto> Handle(GetColorByIdQuery request, CancellationToken cancellationToken)
    {
        var color = await _repository.GetByIdAsync(request.Id);
        if (color is null)
        {
            throw new NotFoundException("Color", request.Id);
        }

        return _mapper.Map<ColorDto>(color);
    }
}

