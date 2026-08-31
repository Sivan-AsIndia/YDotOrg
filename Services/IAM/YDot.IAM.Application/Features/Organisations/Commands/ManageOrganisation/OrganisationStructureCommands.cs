using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Domain.ValueObjects;

namespace YDot.IAM.Application.Features.Organisations.Commands.ManageOrganisation;

/// <summary>Adds a department to the caller's Organisation.</summary>
public sealed record CreateDepartmentCommand(CreateDepartmentRequest Request);

/// <summary>Edits a department.</summary>
public sealed record UpdateDepartmentCommand(Guid Id, UpdateDepartmentRequest Request);

/// <summary>Removes a department. Refused while anybody is in it.</summary>
public sealed record DeleteDepartmentCommand(Guid Id, DeleteStructureRequest Request);

/// <summary>Adds an organisation unit.</summary>
public sealed record CreateOrganisationUnitCommand(CreateOrganisationUnitRequest Request);

/// <summary>Edits an organisation unit.</summary>
public sealed record UpdateOrganisationUnitCommand(Guid Id, UpdateOrganisationUnitRequest Request);

/// <summary>Removes an organisation unit. Refused while anybody is in it.</summary>
public sealed record DeleteOrganisationUnitCommand(Guid Id, DeleteStructureRequest Request);

/// <summary>
/// Departments and organisation units.
///
/// THE THREE RULES THAT RUN THROUGH ALL SIX OPERATIONS
/// ---------------------------------------------------
/// <b>The Organisation is never a parameter.</b> Every read goes through the query filter and
/// every write is stamped by the DbContext, so a department created here belongs to the caller's
/// Organisation and cannot be made to belong to another.
///
/// <b>A code is unique inside the Organisation, and only inside it.</b> Two Organisations may
/// both have a FIN department; that is not a clash, and treating it as one would make codes
/// first-come-first-served across the whole platform.
///
/// <b>Nothing is deleted while it is in use.</b> A department with people in it, or with children
/// beneath it, is refused rather than orphaning either. Deactivating is the way to retire one
/// that still has history attached — which is almost always what was actually wanted.
/// </summary>
public sealed class OrganisationStructureCommandHandler(
    IOrganisationStructureRepository structure,
    IUserRepository users,
    IAuditService audit,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork)
{
    // =====================================================================================
    // Departments
    // =====================================================================================

    public async Task<Result<DepartmentResponse>> HandleAsync(
        CreateDepartmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();
        var request = command.Request;

        var code = CodeValue.TryParse(request.Code);
        if (code is null)
        {
            return Result.Failure<DepartmentResponse>(
                Error.Validation("That code is not valid.",
                    [new ValidationError(nameof(request.Code),
                        "Use letters, digits, hyphens or underscores.")]));
        }

        if (await structure.DepartmentCodeExistsAsync(code.Value, tenantId, null, cancellationToken))
        {
            return Result.Failure<DepartmentResponse>(
                Error.Duplicate("A department in this organisation already uses that code."));
        }

        var parentError = await ValidateDepartmentParentAsync(
            request.ParentDepartmentId, null, cancellationToken);

        if (parentError is not null)
        {
            return Result.Failure<DepartmentResponse>(parentError);
        }

        var department = new Department
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            Code = code.Value,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentDepartmentId = request.ParentDepartmentId,
            HeadUserId = request.HeadUserId,
            DisplayOrder = request.DisplayOrder,
            Status = RecordStatus.Active
        };

        await structure.AddDepartmentAsync(department, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.DepartmentCreated, nameof(Department), department.Id,
            department.Name, new { department.Code }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToResponseAsync(department, cancellationToken));
    }

    public async Task<Result<DepartmentResponse>> HandleAsync(
        UpdateDepartmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();
        var request = command.Request;

        var department = await structure.GetDepartmentAsync(command.Id, cancellationToken);
        if (department is null)
        {
            return Result.Failure<DepartmentResponse>(Error.NotFound("That department was not found."));
        }

        if (department.Version != request.ExpectedVersion)
        {
            return Result.Failure<DepartmentResponse>(Error.Concurrency());
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var code = CodeValue.TryParse(request.Code);

            if (code is null)
            {
                return Result.Failure<DepartmentResponse>(
                    Error.Validation("That code is not valid.",
                        [new ValidationError(nameof(request.Code),
                            "Use letters, digits, hyphens or underscores.")]));
            }

            if (await structure.DepartmentCodeExistsAsync(
                    code.Value, tenantId, department.Id, cancellationToken))
            {
                return Result.Failure<DepartmentResponse>(
                    Error.Duplicate("A department in this organisation already uses that code."));
            }

            department.Code = code.Value;
        }

        if (request.ParentDepartmentId != department.ParentDepartmentId)
        {
            var parentError = await ValidateDepartmentParentAsync(
                request.ParentDepartmentId, department.Id, cancellationToken);

            if (parentError is not null)
            {
                return Result.Failure<DepartmentResponse>(parentError);
            }

            department.ParentDepartmentId = request.ParentDepartmentId;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            department.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            department.Description = request.Description.Trim();
        }

        if (request.HeadUserId.HasValue)
        {
            department.HeadUserId = request.HeadUserId;
        }

        if (request.Status.HasValue)
        {
            department.Status = request.Status.Value;
        }

        if (request.DisplayOrder.HasValue)
        {
            department.DisplayOrder = request.DisplayOrder.Value;
        }

        await audit.WriteAsync(
            AuditActionCodes.DepartmentUpdated, nameof(Department), department.Id,
            department.Name, new { department.Code, department.Status },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToResponseAsync(department, cancellationToken));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteDepartmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();

        var department = await structure.GetDepartmentAsync(command.Id, cancellationToken);
        if (department is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That department was not found."));
        }

        if (department.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        // A department with people in it is refused rather than orphaning them. Deactivating is
        // the way to retire one that still has history attached, which is nearly always what was
        // actually meant.
        var members = await structure.CountDepartmentMembersAsync(department.Id, cancellationToken);
        if (members > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InUse(
                $"{members} person(s) are still in this department. Move them first, or set it "
                + "to inactive instead."));
        }

        var siblings = await structure.GetDepartmentsAsync(tenantId, cancellationToken);
        var children = siblings.Count(item => item.ParentDepartmentId == department.Id);

        if (children > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InUse(
                $"{children} department(s) sit under this one. Move or remove them first."));
        }

        structure.RemoveDepartment(department);

        await audit.WriteAsync(
            AuditActionCodes.DepartmentDeleted, nameof(Department), department.Id,
            department.Name, new { department.Code, command.Request.Reason },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            department.Id, "Deleted", department.Version,
            $"{department.Name} has been removed.", []));
    }

    // =====================================================================================
    // Organisation units
    // =====================================================================================

    public async Task<Result<OrganisationUnitResponse>> HandleAsync(
        CreateOrganisationUnitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();
        var request = command.Request;

        var code = CodeValue.TryParse(request.Code);
        if (code is null)
        {
            return Result.Failure<OrganisationUnitResponse>(
                Error.Validation("That code is not valid.",
                    [new ValidationError(nameof(request.Code),
                        "Use letters, digits, hyphens or underscores.")]));
        }

        if (await structure.UnitCodeExistsAsync(code.Value, tenantId, null, cancellationToken))
        {
            return Result.Failure<OrganisationUnitResponse>(
                Error.Duplicate("A unit in this organisation already uses that code."));
        }

        var parentError = await ValidateUnitParentAsync(request.ParentUnitId, null, cancellationToken);
        if (parentError is not null)
        {
            return Result.Failure<OrganisationUnitResponse>(parentError);
        }

        var unit = new OrganisationUnit
        {
            TenantId = tenantId,
            BusinessUnitId = tenantContext.BusinessUnitId,
            Code = code.Value,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentUnitId = request.ParentUnitId,
            UnitType = request.UnitType?.Trim(),
            AddressLine1 = request.AddressLine1?.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            Country = request.Country?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            ContactEmail = request.ContactEmail?.Trim(),
            ContactPhone = request.ContactPhone?.Trim(),
            TimeZone = request.TimeZone?.Trim(),
            ManagerUserId = request.ManagerUserId,
            DisplayOrder = request.DisplayOrder,
            Status = RecordStatus.Active
        };

        await structure.AddUnitAsync(unit, cancellationToken);

        await audit.WriteAsync(
            AuditActionCodes.OrganisationUnitCreated, nameof(OrganisationUnit), unit.Id,
            unit.Name, new { unit.Code }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToResponseAsync(unit, cancellationToken));
    }

    public async Task<Result<OrganisationUnitResponse>> HandleAsync(
        UpdateOrganisationUnitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();
        var request = command.Request;

        var unit = await structure.GetUnitAsync(command.Id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure<OrganisationUnitResponse>(Error.NotFound("That unit was not found."));
        }

        if (unit.Version != request.ExpectedVersion)
        {
            return Result.Failure<OrganisationUnitResponse>(Error.Concurrency());
        }

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            var code = CodeValue.TryParse(request.Code);

            if (code is null)
            {
                return Result.Failure<OrganisationUnitResponse>(
                    Error.Validation("That code is not valid.",
                        [new ValidationError(nameof(request.Code),
                            "Use letters, digits, hyphens or underscores.")]));
            }

            if (await structure.UnitCodeExistsAsync(code.Value, tenantId, unit.Id, cancellationToken))
            {
                return Result.Failure<OrganisationUnitResponse>(
                    Error.Duplicate("A unit in this organisation already uses that code."));
            }

            unit.Code = code.Value;
        }

        if (request.ParentUnitId != unit.ParentUnitId)
        {
            var parentError = await ValidateUnitParentAsync(
                request.ParentUnitId, unit.Id, cancellationToken);

            if (parentError is not null)
            {
                return Result.Failure<OrganisationUnitResponse>(parentError);
            }

            unit.ParentUnitId = request.ParentUnitId;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            unit.Name = request.Name.Trim();
        }

        if (request.Description is not null) { unit.Description = request.Description.Trim(); }
        if (request.UnitType is not null) { unit.UnitType = request.UnitType.Trim(); }
        if (request.AddressLine1 is not null) { unit.AddressLine1 = request.AddressLine1.Trim(); }
        if (request.AddressLine2 is not null) { unit.AddressLine2 = request.AddressLine2.Trim(); }
        if (request.City is not null) { unit.City = request.City.Trim(); }
        if (request.State is not null) { unit.State = request.State.Trim(); }
        if (request.Country is not null) { unit.Country = request.Country.Trim(); }
        if (request.PostalCode is not null) { unit.PostalCode = request.PostalCode.Trim(); }
        if (request.ContactEmail is not null) { unit.ContactEmail = request.ContactEmail.Trim(); }
        if (request.ContactPhone is not null) { unit.ContactPhone = request.ContactPhone.Trim(); }
        if (request.TimeZone is not null) { unit.TimeZone = request.TimeZone.Trim(); }
        if (request.ManagerUserId.HasValue) { unit.ManagerUserId = request.ManagerUserId; }
        if (request.Status.HasValue) { unit.Status = request.Status.Value; }
        if (request.DisplayOrder.HasValue) { unit.DisplayOrder = request.DisplayOrder.Value; }

        await audit.WriteAsync(
            AuditActionCodes.OrganisationUnitUpdated, nameof(OrganisationUnit), unit.Id,
            unit.Name, new { unit.Code, unit.Status }, cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(await ToResponseAsync(unit, cancellationToken));
    }

    public async Task<Result<OutcomeResponse>> HandleAsync(
        DeleteOrganisationUnitCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tenantId = tenantContext.RequireTenantId();

        var unit = await structure.GetUnitAsync(command.Id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure<OutcomeResponse>(Error.NotFound("That unit was not found."));
        }

        if (unit.Version != command.Request.ExpectedVersion)
        {
            return Result.Failure<OutcomeResponse>(Error.Concurrency());
        }

        var members = await structure.CountUnitMembersAsync(unit.Id, cancellationToken);
        if (members > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InUse(
                $"{members} person(s) are still in this unit. Move them first, or set it to "
                + "inactive instead."));
        }

        var siblings = await structure.GetUnitsAsync(tenantId, cancellationToken);
        var children = siblings.Count(item => item.ParentUnitId == unit.Id);

        if (children > 0)
        {
            return Result.Failure<OutcomeResponse>(Error.InUse(
                $"{children} unit(s) sit under this one. Move or remove them first."));
        }

        structure.RemoveUnit(unit);

        await audit.WriteAsync(
            AuditActionCodes.OrganisationUnitDeleted, nameof(OrganisationUnit), unit.Id,
            unit.Name, new { unit.Code, command.Request.Reason },
            cancellationToken: cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OutcomeResponse(
            unit.Id, "Deleted", unit.Version, $"{unit.Name} has been removed.", []));
    }

    // =====================================================================================
    // Shared checks
    // =====================================================================================

    /// <summary>
    /// Checks a proposed parent.
    ///
    /// TWO WAYS TO BREAK A TREE, and both are checked: naming a parent that does not exist in
    /// this Organisation, and naming one that is a descendant of the node being moved. The second
    /// makes a cycle, and a cycle turns every later walk of the tree into an infinite loop —
    /// including the one that renders the screen.
    /// </summary>
    private async Task<Error?> ValidateDepartmentParentAsync(
        Guid? parentId, Guid? movingId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue)
        {
            return null;
        }

        if (parentId == movingId)
        {
            return Error.Validation("A department cannot sit under itself.",
                [new ValidationError("ParentDepartmentId", "Choose a different parent.")]);
        }

        var parent = await structure.GetDepartmentAsync(parentId.Value, cancellationToken);

        // Not found means not in THIS Organisation: the query filter has already excluded
        // everybody else's, so there is nothing more to check.
        if (parent is null)
        {
            return Error.NotFound("That parent department was not found in this organisation.");
        }

        if (movingId.HasValue)
        {
            var all = await structure.GetDepartmentsAsync(
                tenantContext.RequireTenantId(), cancellationToken);

            var walker = parent;
            var guard = 0;

            while (walker?.ParentDepartmentId is not null && guard++ < 64)
            {
                if (walker.ParentDepartmentId == movingId)
                {
                    return Error.Validation(
                        "That would put this department under one of its own children.",
                        [new ValidationError("ParentDepartmentId", "Choose a different parent.")]);
                }

                walker = all.FirstOrDefault(item => item.Id == walker.ParentDepartmentId);
            }
        }

        return null;
    }

    private async Task<Error?> ValidateUnitParentAsync(
        Guid? parentId, Guid? movingId, CancellationToken cancellationToken)
    {
        if (!parentId.HasValue)
        {
            return null;
        }

        if (parentId == movingId)
        {
            return Error.Validation("A unit cannot sit under itself.",
                [new ValidationError("ParentUnitId", "Choose a different parent.")]);
        }

        var parent = await structure.GetUnitAsync(parentId.Value, cancellationToken);

        if (parent is null)
        {
            return Error.NotFound("That parent unit was not found in this organisation.");
        }

        if (movingId.HasValue)
        {
            var all = await structure.GetUnitsAsync(tenantContext.RequireTenantId(), cancellationToken);

            var walker = parent;
            var guard = 0;

            while (walker?.ParentUnitId is not null && guard++ < 64)
            {
                if (walker.ParentUnitId == movingId)
                {
                    return Error.Validation(
                        "That would put this unit under one of its own children.",
                        [new ValidationError("ParentUnitId", "Choose a different parent.")]);
                }

                walker = all.FirstOrDefault(item => item.Id == walker.ParentUnitId);
            }
        }

        return null;
    }

    private async Task<DepartmentResponse> ToResponseAsync(
        Department department, CancellationToken cancellationToken)
    {
        var all = await structure.GetDepartmentsAsync(
            tenantContext.RequireTenantId(), cancellationToken);

        var parent = department.ParentDepartmentId.HasValue
            ? all.FirstOrDefault(item => item.Id == department.ParentDepartmentId)
            : null;

        var head = department.HeadUserId.HasValue
            ? await users.GetByIdAsync(department.HeadUserId.Value, cancellationToken)
            : null;

        return new DepartmentResponse(
            department.Id,
            department.Code,
            department.Name,
            department.Description,
            department.ParentDepartmentId,
            parent?.Name,
            department.HeadUserId,
            head?.DisplayName,
            department.Status,
            department.DisplayOrder,
            await structure.CountDepartmentMembersAsync(department.Id, cancellationToken),
            all.Count(item => item.ParentDepartmentId == department.Id),
            department.Version);
    }

    private async Task<OrganisationUnitResponse> ToResponseAsync(
        OrganisationUnit unit, CancellationToken cancellationToken)
    {
        var all = await structure.GetUnitsAsync(tenantContext.RequireTenantId(), cancellationToken);

        var parent = unit.ParentUnitId.HasValue
            ? all.FirstOrDefault(item => item.Id == unit.ParentUnitId)
            : null;

        var manager = unit.ManagerUserId.HasValue
            ? await users.GetByIdAsync(unit.ManagerUserId.Value, cancellationToken)
            : null;

        return new OrganisationUnitResponse(
            unit.Id,
            unit.Code,
            unit.Name,
            unit.Description,
            unit.ParentUnitId,
            parent?.Name,
            unit.UnitType,
            unit.AddressLine1,
            unit.AddressLine2,
            unit.City,
            unit.State,
            unit.Country,
            unit.PostalCode,
            unit.ContactEmail,
            unit.ContactPhone,
            unit.TimeZone,
            unit.ManagerUserId,
            manager?.DisplayName,
            unit.Status,
            unit.DisplayOrder,
            await structure.CountUnitMembersAsync(unit.Id, cancellationToken),
            all.Count(item => item.ParentUnitId == unit.Id),
            unit.Version);
    }
}

/// <summary>Lists the departments in the caller's Organisation.</summary>
public sealed record GetDepartmentsQuery;

/// <summary>Lists the organisation units in the caller's Organisation.</summary>
public sealed record GetOrganisationUnitsQuery;

/// <summary>
/// The read side of the structural masters.
///
/// Separate from the command handler because the management screens read far more often than
/// they write, and a read that does not drag the write dependencies along is cheaper to serve
/// and easier to reason about.
/// </summary>
public sealed class OrganisationStructureQueryHandler(
    IOrganisationStructureRepository structure,
    IUserRepository users,
    ITenantContext tenantContext)
{
    public async Task<Result<IReadOnlyList<DepartmentResponse>>> HandleAsync(
        GetDepartmentsQuery query, CancellationToken cancellationToken)
    {
        _ = query;

        var tenantId = tenantContext.RequireTenantId();
        var departments = await structure.GetDepartmentsAsync(tenantId, cancellationToken);

        var responses = new List<DepartmentResponse>(departments.Count);

        foreach (var department in departments.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name))
        {
            var parent = department.ParentDepartmentId.HasValue
                ? departments.FirstOrDefault(item => item.Id == department.ParentDepartmentId)
                : null;

            var head = department.HeadUserId.HasValue
                ? await users.GetByIdAsync(department.HeadUserId.Value, cancellationToken)
                : null;

            responses.Add(new DepartmentResponse(
                department.Id,
                department.Code,
                department.Name,
                department.Description,
                department.ParentDepartmentId,
                parent?.Name,
                department.HeadUserId,
                head?.DisplayName,
                department.Status,
                department.DisplayOrder,
                await structure.CountDepartmentMembersAsync(department.Id, cancellationToken),
                departments.Count(item => item.ParentDepartmentId == department.Id),
                department.Version));
        }

        return Result.Success<IReadOnlyList<DepartmentResponse>>(responses);
    }

    public async Task<Result<IReadOnlyList<OrganisationUnitResponse>>> HandleAsync(
        GetOrganisationUnitsQuery query, CancellationToken cancellationToken)
    {
        _ = query;

        var tenantId = tenantContext.RequireTenantId();
        var units = await structure.GetUnitsAsync(tenantId, cancellationToken);

        var responses = new List<OrganisationUnitResponse>(units.Count);

        foreach (var unit in units.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name))
        {
            var parent = unit.ParentUnitId.HasValue
                ? units.FirstOrDefault(item => item.Id == unit.ParentUnitId)
                : null;

            var manager = unit.ManagerUserId.HasValue
                ? await users.GetByIdAsync(unit.ManagerUserId.Value, cancellationToken)
                : null;

            responses.Add(new OrganisationUnitResponse(
                unit.Id,
                unit.Code,
                unit.Name,
                unit.Description,
                unit.ParentUnitId,
                parent?.Name,
                unit.UnitType,
                unit.AddressLine1,
                unit.AddressLine2,
                unit.City,
                unit.State,
                unit.Country,
                unit.PostalCode,
                unit.ContactEmail,
                unit.ContactPhone,
                unit.TimeZone,
                unit.ManagerUserId,
                manager?.DisplayName,
                unit.Status,
                unit.DisplayOrder,
                await structure.CountUnitMembersAsync(unit.Id, cancellationToken),
                units.Count(item => item.ParentUnitId == unit.Id),
                unit.Version));
        }

        return Result.Success<IReadOnlyList<OrganisationUnitResponse>>(responses);
    }
}
