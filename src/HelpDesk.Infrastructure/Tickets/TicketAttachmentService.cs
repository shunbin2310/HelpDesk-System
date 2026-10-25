using HelpDesk.Application.Authentication;
using HelpDesk.Application.Tickets;
using HelpDesk.Domain.Constants;
using HelpDesk.Infrastructure.Persistence;
using HelpDesk.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Infrastructure.Tickets;

public sealed class TicketAttachmentService(HelpDeskDbContext db, ICurrentUserContext current, IAttachmentStore store) : ITicketAttachmentService
{
    public async Task<TicketResult<IReadOnlyList<AttachmentItem>>> ListAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(ticketId, cancellationToken))
            return new(TicketResultStatus.NotFound, Error: "Ticket was not found.");
        var attachments = await db.Attachments.AsNoTracking().Where(x => x.TicketId == ticketId && x.TicketMessageId == null)
            .OrderBy(x => x.Id).Select(x => new AttachmentItem(x.Id, x.OriginalFileName, x.ContentType, x.SizeBytes)).ToListAsync(cancellationToken);
        return new(TicketResultStatus.Success, attachments);
    }

    public async Task<TicketResult<AttachmentDownload>> DownloadAsync(long ticketId, long attachmentId, CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(ticketId, cancellationToken))
            return MissingFile();
        // Retired message attachments stay in storage, but are not exposed as submission attachments.
        var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId
            && x.TicketId == ticketId && x.TicketMessageId == null, cancellationToken);
        if (attachment is null)
            return MissingFile();
        try
        {
            return new(TicketResultStatus.Success, new(store.OpenRead(attachment.StorageKey), attachment.OriginalFileName, attachment.ContentType));
        }
        catch (FileNotFoundException) { return MissingFile(); }
        catch (DirectoryNotFoundException) { return MissingFile(); }
    }

    private async Task<bool> CanViewAsync(long ticketId, CancellationToken ct)
    {
        var actor = await WorkflowActorReader.ReadAsync(db, current.UserId, ct);
        var ticket = await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId, ct);
        return actor is not null && ticket is not null && TicketWorkflowPolicy.CanView(ticket, actor.Id, actor.Role);
    }

    private static TicketResult<AttachmentDownload> MissingFile() => new(TicketResultStatus.NotFound, Error: "Attachment was not found.");
}
