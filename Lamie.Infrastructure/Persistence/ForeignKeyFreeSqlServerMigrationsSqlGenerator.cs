using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;

namespace Lamie.Infrastructure.Persistence;

/// <summary>
/// Keeps EF relationship metadata available for navigation loading and client-side
/// cascade tracking while preventing SQL Server DDL from creating physical foreign
/// key constraints.
/// </summary>
public sealed class ForeignKeyFreeSqlServerMigrationsSqlGenerator
    : SqlServerMigrationsSqlGenerator
{
    public ForeignKeyFreeSqlServerMigrationsSqlGenerator(
        MigrationsSqlGeneratorDependencies dependencies,
        ICommandBatchPreparer commandBatchPreparer)
        : base(dependencies, commandBatchPreparer)
    {
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        if (operation.ForeignKeys.Count == 0)
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        var foreignKeys = operation.ForeignKeys.ToArray();
        operation.ForeignKeys.Clear();
        try
        {
            base.Generate(operation, model, builder, terminate);
        }
        finally
        {
            foreach (var foreignKey in foreignKeys)
            {
                operation.ForeignKeys.Add(foreignKey);
            }
        }
    }
}
