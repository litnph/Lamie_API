using Lamie.Application.Common.Exceptions;
using Lamie.Application.Common.Persistence;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using MediatR;

namespace Lamie.Application.Settings.Attributes.Languages;

// Commands
public sealed record CreateLanguageCommand(string Code, string Name, bool IsActive) : IRequest;

public sealed record UpdateLanguageCommand(string Code, string Name, bool IsActive) : IRequest;

public sealed record DeleteLanguageCommand(string Code) : IRequest;

// Command Handlers
public sealed class CreateLanguageHandler : IRequestHandler<CreateLanguageCommand>
{
    private readonly ILanguageRepository _repository;

    public CreateLanguageHandler(ILanguageRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(CreateLanguageCommand request, CancellationToken cancellationToken)
    {
        var exists = await _repository.ExistsAsync(request.Code, cancellationToken);
        if (exists)
        {
            throw new ConflictException($"Language '{request.Code}' already exists.");
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["code"] = ["Code is required"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required"]
            });
        }

        var language = new Language(request.Code, request.Name, request.IsActive);
        await _repository.AddAsync(language, cancellationToken);
    }
}

public sealed class UpdateLanguageHandler : IRequestHandler<UpdateLanguageCommand>
{
    private readonly ILanguageRepository _repository;

    public UpdateLanguageHandler(ILanguageRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(UpdateLanguageCommand request, CancellationToken cancellationToken)
    {
        var language = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (language is null)
        {
            throw new NotFoundException("Language", request.Code);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required"]
            });
        }

        // Name + IsActive là mutable
        typeof(Language).GetProperty(nameof(Language.Name))!
            .SetValue(language, request.Name);
        language.SetActive(request.IsActive);

        await _repository.UpdateAsync(language, cancellationToken);
    }
}

public sealed class DeleteLanguageHandler : IRequestHandler<DeleteLanguageCommand>
{
    private readonly ILanguageRepository _repository;
    private readonly IReferentialIntegrityService _referentialIntegrity;

    public DeleteLanguageHandler(
        ILanguageRepository repository,
        IReferentialIntegrityService referentialIntegrity)
    {
        _repository = repository;
        _referentialIntegrity = referentialIntegrity;
    }

    public async Task Handle(DeleteLanguageCommand request, CancellationToken cancellationToken)
    {
        var language = await _repository.GetByCodeAsync(request.Code, cancellationToken);
        if (language is null)
        {
            throw new NotFoundException("Language", request.Code);
        }

        await _referentialIntegrity.EnsureCanDeleteAsync(
            ReferencedMasterData.Language,
            request.Code,
            cancellationToken);
        await _repository.DeleteAsync(language, cancellationToken);
    }
}

