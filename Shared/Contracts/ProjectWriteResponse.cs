using Shared.Models;

namespace Shared.Contracts;

public sealed record ProjectWriteResponse(ProjectRecord Project, string Revision);
