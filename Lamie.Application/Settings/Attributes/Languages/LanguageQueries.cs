using AutoMapper;
using Lamie.Application.Common.Exceptions;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Languages;

// Queries
public sealed record GetAllLanguagesQuery() : IRequest<List<LanguageDto>>;

public sealed record GetLanguageByCodeQuery(string Code) : IRequest<LanguageDto>;

// Query Handlers
public sealed class GetAllLanguagesHandler : IRequestHandler<GetAllLanguagesQuery, List<LanguageDto>>
{
    private readonly ILanguageRepository _repository;
    private readonly IMapper _mapper;

    public GetAllLanguagesHandler(ILanguageRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<LanguageDto>> Handle(GetAllLanguagesQuery request, CancellationToken cancellationToken)
    {
        var languages = await _repository.GetAllAsync(cancellationToken);
        return languages.Select(_mapper.Map<LanguageDto>).ToList();
    }
}

public sealed class GetLanguageByCodeHandler : IRequestHandler<GetLanguageByCodeQuery, LanguageDto>
{
    private readonly ILanguageRepository _repository;
    private readonly IMapper _mapper;

    public GetLanguageByCodeHandler(ILanguageRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<LanguageDto> Handle(GetLanguageByCodeQuery request, CancellationToken cancellationToken)
    {
        var language = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (language is null)
        {
            throw new NotFoundException("Language", request.Code);
        }

        return _mapper.Map<LanguageDto>(language);
    }
}

