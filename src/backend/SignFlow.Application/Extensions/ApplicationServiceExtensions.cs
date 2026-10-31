using SignFlow.Application.Common.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Application.Common.Models;
using SignFlow.Application.Features.Auth.Commands;
using SignFlow.Application.Features.Documents.Commands;
using SignFlow.Application.Features.Documents.Queries;
using SignFlow.Application.Features.Reviews;
using SignFlow.Application.Features.SignatureFields;
using SignFlow.Application.Features.SignatureFields.Commands;
using SignFlow.Application.Features.Recipients.Commands;
using SignFlow.Application.Features.Checklist.Commands;
using SignFlow.Application.Features.Signing.Commands;
using SignFlow.Application.Features.Signing.Queries;
using SignFlow.Application.Features.AuditTrail.Queries;
using SignFlow.Application.Features.Administration.Commands;
using SignFlow.Application.Features.Administration.Queries;
using SignFlow.Application.Features.Profile.Commands;
using SignFlow.Application.Features.Profile.Queries;
using SignFlow.Application.Features.Notifications.Commands;
using SignFlow.Application.Features.Notifications.Queries;
using SignFlow.Application.Features.Comments;
using SignFlow.Application.Features.Comments.Commands;
using SignFlow.Application.Features.Comments.Queries;
using SignFlow.Application.Features.Reports.Queries;
using SignFlow.Application.Features.Requests.Queries;
using SignFlow.Application.Features.RequestTypes.Commands;
using SignFlow.Application.Features.RequestTypes.Queries;
using SignFlow.Application.Features.WorkflowPresets;
using SignFlow.Application.Features.WorkflowPresets.Commands;
using SignFlow.Application.Features.WorkflowPresets.Queries;
using SignFlow.Application.Features.Vacations.Commands;
using SignFlow.Application.Features.Vacations.Queries;
using SignFlow.Application.Features.Gdpr.Commands;
using SignFlow.Application.Features.Gdpr.Queries;
using SignFlow.Application.Features.HtmlTemplates.Commands;
using SignFlow.Application.Features.HtmlTemplates.Queries;
using SignFlow.Application.Features.PdfTemplates.Commands;
using SignFlow.Application.Features.PdfTemplates.Queries;
using SignFlow.Application.Features.Clauses.Commands;
using SignFlow.Application.Features.Clauses.Queries;
using SignFlow.Application.Features.RegulatoryWatch.Commands;
using SignFlow.Application.Features.RegulatoryWatch.Queries;
using SignFlow.Application.Services;

namespace SignFlow.Application.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // FluentValidation
        services.AddValidatorsFromAssemblyContaining<LoginCommandValidator>();

        // Auth command handlers
        // Auth: sessions, two-factor, invitations, recovery (rework spec M1)
        services.AddScoped<SignFlow.Application.Features.Auth.AuthSessions>();
        services.AddScoped<PasswordResetMail>();
        services.AddScoped<SignFlow.Application.Common.Services.PdfUploadGuard>();
        services.AddScoped<ICommandHandler<LoginCommand, SignFlow.Application.Features.Auth.LoginOutcome>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<MfaLoginCommand, SignFlow.Application.Features.Auth.AuthSession>, MfaLoginCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshTokenCommand, SignFlow.Application.Features.Auth.AuthSession>, RefreshTokenCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand>, LogoutCommandHandler>();
        services.AddScoped<ICommandHandler<AcceptInvitationCommand, SignFlow.Application.Features.Auth.AuthSession>, AcceptInvitationCommandHandler>();
        services.AddScoped<ICommandHandler<InviteUserCommand, InvitationDto>, InviteUserCommandHandler>();
        services.AddScoped<ICommandHandler<RevokeInvitationCommand>, RevokeInvitationCommandHandler>();
        services.AddScoped<IQueryHandler<GetInvitationsQuery, List<InvitationDto>>, GetInvitationsQueryHandler>();
        services.AddScoped<ICommandHandler<BeginMfaSetupCommand, MfaSetupDto>, BeginMfaSetupCommandHandler>();
        services.AddScoped<ICommandHandler<ConfirmMfaSetupCommand, IReadOnlyList<string>>, ConfirmMfaSetupCommandHandler>();
        services.AddScoped<ICommandHandler<DisableMfaCommand>, DisableMfaCommandHandler>();
        services.AddScoped<IQueryHandler<SignFlow.Application.Features.Profile.GetMySessionsQuery, List<SignFlow.Application.Features.Profile.SessionDto>>, SignFlow.Application.Features.Profile.GetMySessionsQueryHandler>();
        services.AddScoped<ICommandHandler<SignFlow.Application.Features.Profile.EndMySessionCommand>, SignFlow.Application.Features.Profile.EndMySessionCommandHandler>();
        services.AddScoped<ICommandHandler<ForgotPasswordCommand>, ForgotPasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ResetPasswordCommand>, ResetPasswordCommandHandler>();

        // Document command handlers
        services.AddScoped<ICommandHandler<UploadDocumentCommand, DocumentUploadResult>, UploadDocumentCommandHandler>();
        services.AddScoped<ICommandHandler<SendDocumentCommand>, SendDocumentCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteDocumentCommand>, DeleteDocumentCommandHandler>();
        services.AddScoped<ICommandHandler<ReopenDocumentCommand>, ReopenDocumentCommandHandler>();
        services.AddScoped<IContractTextProvider, ContractTextProvider>();
        services.AddScoped<IContractReviewService, ContractReviewService>();
        services.AddScoped<ICommandHandler<UploadRevisionCommand, UploadRevisionResult>, UploadRevisionCommandHandler>();
        services.AddScoped<ICommandHandler<EditDocumentPdfCommand, UploadRevisionResult>, EditDocumentPdfCommandHandler>();
        services.AddScoped<ICommandHandler<EditPdfTemplateCommand, EditPdfTemplateResult>, EditPdfTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<ReviseFromTemplateCommand, UploadRevisionResult>, ReviseFromTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateObligationCommand>, UpdateObligationCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateContractTermCommand>, UpdateContractTermCommandHandler>();
        services.AddScoped<ICommandHandler<AskContractQuestionCommand, ContractChatReplyDto>, AskContractQuestionCommandHandler>();
        services.AddScoped<ICommandHandler<ExtractKeyTermsCommand, KeyTermsDto>, ExtractKeyTermsCommandHandler>();
        services.AddScoped<ICommandHandler<WriteRenewalNoteCommand, RenewalNoteDto>, WriteRenewalNoteCommandHandler>();

        // Pre-flight review (M8)
        services.AddScoped<ICommandHandler<StartReviewCommand, ReviewRunDto>, StartReviewCommandHandler>();
        services.AddScoped<ICommandHandler<DecideReviewItemCommand, ReviewDto>, DecideReviewItemCommandHandler>();
        services.AddScoped<IQueryHandler<GetLatestReviewQuery, ReviewDto>, GetLatestReviewQueryHandler>();
        services.AddScoped<IQueryHandler<GetReviewSettingsQuery, List<ReviewRuleSettingDto>>, GetReviewSettingsQueryHandler>();
        services.AddScoped<ICommandHandler<SetReviewSettingCommand>, SetReviewSettingCommandHandler>();
        services.AddScoped<IReviewRunner, ReviewRunner>();
        services.AddScoped<IReviewFeedbackSender, ReviewFeedbackSender>();
        services.AddScoped<IContractIntake, ContractIntake>();

        // Signing access (M5): one gate for every signing endpoint
        services.AddScoped<SignFlow.Application.Features.Signing.ISigningAccess, SignFlow.Application.Features.Signing.SigningAccess>();
        services.AddScoped<ICommandHandler<ResendSigningLinkCommand>, ResendSigningLinkCommandHandler>();

        // Circuits (M4): pools, stand-ins, delegation
        services.AddScoped<ICircuitDirectory, CircuitDirectory>();
        services.AddScoped<ICommandHandler<DelegateStepCommand, DelegatedStepDto>, DelegateStepCommandHandler>();

        // Seal and public verification (R-M5-8, R-M5-9)
        services.AddScoped<IDocumentSealer, DocumentSealer>();
        services.AddScoped<IQueryHandler<VerifySealQuery, SealInfoDto>, VerifySealQueryHandler>();
        services.AddScoped<ICommandHandler<CheckSealedFileCommand, SealCheckDto>, CheckSealedFileCommandHandler>();

        // Document query handlers
        services.AddScoped<IQueryHandler<GetDocumentsQuery, PagedResult<DocumentSummaryDto>>, GetDocumentsQueryHandler>();
        services.AddScoped<IQueryHandler<GetDocumentByIdQuery, DocumentDetailDto>, GetDocumentByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetDashboardQuery, DashboardDto>, GetDashboardQueryHandler>();
        services.AddScoped<IQueryHandler<GetDocumentSummaryQuery, AiDocumentSummaryDto>, GetDocumentSummaryQueryHandler>();
        services.AddScoped<IQueryHandler<GetTemplateRevisionFormQuery, TemplateRevisionFormDto>, GetTemplateRevisionFormQueryHandler>();
        services.AddScoped<IQueryHandler<GetDocumentVersionFileQuery, DocumentVersionFileDto>, GetDocumentVersionFileQueryHandler>();
        services.AddScoped<IQueryHandler<CompareDocumentVersionsQuery, VersionComparisonDto>, CompareDocumentVersionsQueryHandler>();


        // Field command handlers
        services.AddScoped<ICommandHandler<SaveFieldsCommand, int>, SaveFieldsCommandHandler>();
        services.AddScoped<IQueryHandler<GetFieldLayoutReportQuery, List<FieldOutsidePageDto>>, GetFieldLayoutReportQueryHandler>();
        services.AddScoped<IQueryHandler<DetectContractFieldsQuery, DetectedFormFields>, DetectContractFieldsQueryHandler>();

        // Recipient command handlers
        services.AddScoped<ICommandHandler<ManageRecipientsCommand>, ManageRecipientsCommandHandler>();

        // Checklist command handlers (collab Phase 4 — review/approval prerequisites)
        services.AddScoped<ICommandHandler<SetChecklistItemsCommand>, SetChecklistItemsCommandHandler>();
        services.AddScoped<ICommandHandler<ToggleChecklistItemCommand>, ToggleChecklistItemCommandHandler>();

        // Signing handlers
        services.AddScoped<ICommandHandler<SignFieldCommand, SignFieldResult>, SignFieldCommandHandler>();
        services.AddScoped<ICommandHandler<ApproveSigningCommand, ApproveSigningResult>, ApproveSigningCommandHandler>();
        services.AddScoped<ICommandHandler<RejectDocumentCommand>, RejectDocumentCommandHandler>();
        services.AddScoped<ICommandHandler<AcknowledgeReviewCommand>, AcknowledgeReviewCommandHandler>();
        services.AddScoped<ICommandHandler<RequestChangesCommand>, RequestChangesCommandHandler>();
        services.AddScoped<IQueryHandler<GetSigningSessionQuery, SigningSessionDto>, GetSigningSessionQueryHandler>();

        // Reports (admin/manager analytics)
        services.AddScoped<IQueryHandler<GetAdminReportQuery, AdminReportDto>, GetAdminReportQueryHandler>();
        services.AddScoped<IQueryHandler<GetRiskMapQuery, RiskMapDto>, GetRiskMapQueryHandler>();
        services.AddScoped<IQueryHandler<GetContractLifecycleQuery, ContractLifecycleDto>, GetContractLifecycleQueryHandler>();
        services.AddScoped<IQueryHandler<GetContractScheduleQuery, List<ContractScheduleRowDto>>, GetContractScheduleQueryHandler>();

        // Audit
        services.AddScoped<IQueryHandler<GetAuditTrailQuery, PagedResult<AuditLogDto>>, GetAuditTrailQueryHandler>();
        services.AddScoped<IQueryHandler<ExportAuditTrailQuery, byte[]>, ExportAuditTrailQueryHandler>();
        services.AddScoped<IQueryHandler<GetOrgAuditLogQuery, PagedResult<OrgAuditLogDto>>, GetOrgAuditLogQueryHandler>();

        // Templates

        // Administration
        services.AddScoped<ICommandHandler<UpdateUserCommand>, UpdateUserCommandHandler>();
        services.AddScoped<ICommandHandler<ResetUserPasswordCommand>, ResetUserPasswordCommandHandler>();
        services.AddScoped<IQueryHandler<GetUsersQuery, PagedResult<UserDto>>, GetUsersQueryHandler>();
        services.AddScoped<IQueryHandler<GetRolesQuery, List<string>>, GetRolesQueryHandler>();

        // Notifications
        services.AddScoped<IQueryHandler<GetMyNotificationsQuery, NotificationsResultDto>, GetMyNotificationsQueryHandler>();
        services.AddScoped<ICommandHandler<MarkNotificationsReadCommand>, MarkNotificationsReadCommandHandler>();

        // Comments (collaboration layer)
        services.AddScoped<ICommandHandler<AddCommentCommand, CommentDto>, AddCommentCommandHandler>();
        services.AddScoped<ICommandHandler<AddSigningCommentCommand, CommentDto>, AddSigningCommentCommandHandler>();
        services.AddScoped<ICommandHandler<ResolveCommentCommand>, ResolveCommentCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteCommentCommand>, DeleteCommentCommandHandler>();
        services.AddScoped<IQueryHandler<GetDocumentCommentsQuery, List<CommentDto>>, GetDocumentCommentsQueryHandler>();
        services.AddScoped<IQueryHandler<GetSigningCommentsQuery, List<CommentDto>>, GetSigningCommentsQueryHandler>();

        // My Requests (personal workflow tracking)
        services.AddScoped<IQueryHandler<GetMyRequestsQuery, MyRequestsDto>, GetMyRequestsQueryHandler>();

        // Request Types & SLA
        services.AddScoped<IQueryHandler<GetRequestTypesQuery, List<RequestTypeDto>>, GetRequestTypesQueryHandler>();
        services.AddScoped<IQueryHandler<GetActiveRequestTypesQuery, List<RequestTypeDto>>, GetActiveRequestTypesQueryHandler>();
        services.AddScoped<IQueryHandler<GetSlaMetricsQuery, SlaMetricsDto>, GetSlaMetricsQueryHandler>();
        services.AddScoped<ICommandHandler<CreateRequestTypeCommand, Guid>, CreateRequestTypeCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateRequestTypeCommand>, UpdateRequestTypeCommandHandler>();
        services.AddScoped<ICommandHandler<SetRequestTypeStatusCommand>, SetRequestTypeStatusCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteRequestTypeCommand>, DeleteRequestTypeCommandHandler>();

        // Clause library
        services.AddScoped<IQueryHandler<GetClausesQuery, List<ClauseDto>>, GetClausesQueryHandler>();
        services.AddScoped<IQueryHandler<GetApprovedClausesQuery, List<ClauseDto>>, GetApprovedClausesQueryHandler>();
        services.AddScoped<ICommandHandler<CreateClauseCommand, Guid>, CreateClauseCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateClauseCommand>, UpdateClauseCommandHandler>();
        services.AddScoped<ICommandHandler<SetClauseStatusCommand>, SetClauseStatusCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteClauseCommand>, DeleteClauseCommandHandler>();
        services.AddScoped<ICommandHandler<RecordClauseUsageCommand>, RecordClauseUsageCommandHandler>();

        // Regulatory watch
        services.AddScoped<IQueryHandler<GetRegulatoryTextsQuery, List<RegulatoryTextDto>>, GetRegulatoryTextsQueryHandler>();
        services.AddScoped<IQueryHandler<GetPublishedRegulatoryTextsQuery, List<RegulatoryTextDto>>, GetPublishedRegulatoryTextsQueryHandler>();
        services.AddScoped<ICommandHandler<CreateRegulatoryTextCommand, Guid>, CreateRegulatoryTextCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateRegulatoryTextCommand>, UpdateRegulatoryTextCommandHandler>();
        services.AddScoped<ICommandHandler<SetRegulatoryTextStatusCommand>, SetRegulatoryTextStatusCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteRegulatoryTextCommand>, DeleteRegulatoryTextCommandHandler>();
        services.AddScoped<IQueryHandler<PreviewRegulatoryArticlesQuery, ArticleSplit>, PreviewRegulatoryArticlesQueryHandler>();
        services.AddScoped<IQueryHandler<GetRegulatoryArticlesQuery, List<RegulatoryArticleDto>>, GetRegulatoryArticlesQueryHandler>();
        services.AddScoped<ICommandHandler<SaveRegulatoryArticlesCommand>, SaveRegulatoryArticlesCommandHandler>();
        services.AddScoped<ICommandHandler<VerifyRegulatoryTextCommand>, VerifyRegulatoryTextCommandHandler>();

        // Vacations
        services.AddScoped<IQueryHandler<GetMyVacationsQuery, List<VacationDto>>, GetMyVacationsQueryHandler>();
        services.AddScoped<IQueryHandler<CheckRecipientAvailabilityQuery, RecipientAvailabilityDto>, CheckRecipientAvailabilityQueryHandler>();
        services.AddScoped<ICommandHandler<ScheduleVacationCommand, VacationDto>, ScheduleVacationCommandHandler>();
        services.AddScoped<ICommandHandler<CancelVacationCommand>, CancelVacationCommandHandler>();

        // Profile (self-service)
        services.AddScoped<IQueryHandler<GetMyProfileQuery, ProfileDto>, GetMyProfileQueryHandler>();
        services.AddScoped<ICommandHandler<UpdateProfileCommand, ProfileDto>, UpdateProfileCommandHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand>, ChangePasswordCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateAvatarCommand, string>, UpdateAvatarCommandHandler>();

        // HTML Templates
        services.AddScoped<ICommandHandler<GenerateHtmlTemplateCommand, GenerateHtmlTemplateResult>, GenerateHtmlTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<ReviewHtmlTemplateCommand, ReviewHtmlTemplateResult>, ReviewHtmlTemplateCommandHandler>();
        services.AddScoped<IWorkflowPresetResolver, WorkflowPresetResolver>();
        services.AddScoped<IQueryHandler<GetWorkflowPresetsQuery, List<WorkflowPresetDto>>, GetWorkflowPresetsQueryHandler>();
        services.AddScoped<ICommandHandler<CreateWorkflowPresetCommand, Guid>, CreateWorkflowPresetCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateWorkflowPresetCommand>, UpdateWorkflowPresetCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteWorkflowPresetCommand>, DeleteWorkflowPresetCommandHandler>();

        // Teams and the working calendar (R-M4-4, R-M4-10)
        services.AddScoped<IQueryHandler<SignFlow.Application.Features.Teams.GetTeamsQuery, List<SignFlow.Application.Features.Teams.TeamDto>>, SignFlow.Application.Features.Teams.GetTeamsQueryHandler>();
        services.AddScoped<ICommandHandler<SignFlow.Application.Features.Teams.SaveTeamCommand, Guid>, SignFlow.Application.Features.Teams.SaveTeamCommandHandler>();
        services.AddScoped<ICommandHandler<SignFlow.Application.Features.Teams.DeleteTeamCommand>, SignFlow.Application.Features.Teams.DeleteTeamCommandHandler>();
        services.AddScoped<IQueryHandler<SignFlow.Application.Features.Teams.GetHolidaysQuery, List<SignFlow.Application.Features.Teams.HolidayDto>>, SignFlow.Application.Features.Teams.GetHolidaysQueryHandler>();
        services.AddScoped<ICommandHandler<SignFlow.Application.Features.Teams.AddHolidayCommand, Guid>, SignFlow.Application.Features.Teams.AddHolidayCommandHandler>();
        services.AddScoped<ICommandHandler<SignFlow.Application.Features.Teams.DeleteHolidayCommand>, SignFlow.Application.Features.Teams.DeleteHolidayCommandHandler>();
        services.AddScoped<ICommandHandler<CreateHtmlTemplateCommand, Guid>, CreateHtmlTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateHtmlTemplateCommand>, UpdateHtmlTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<DeleteHtmlTemplateCommand>, DeleteHtmlTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<CreateRequestFromHtmlTemplateCommand, Guid>, CreateRequestFromHtmlTemplateCommandHandler>();
        services.AddScoped<IQueryHandler<GetHtmlTemplatesQuery, PagedResult<HtmlTemplateDto>>, GetHtmlTemplatesQueryHandler>();
        services.AddScoped<IQueryHandler<GetHtmlTemplateByIdQuery, HtmlTemplateDetailDto>, GetHtmlTemplateByIdQueryHandler>();

        // PDF templates (3rd request-origination flow: persisted PDF + coordinate-mapped fields)
        services.AddScoped<ICommandHandler<CreatePdfTemplateCommand, CreatePdfTemplateResult>, CreatePdfTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<UpdatePdfTemplateCommand>, UpdatePdfTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<DeletePdfTemplateCommand>, DeletePdfTemplateCommandHandler>();
        services.AddScoped<ICommandHandler<CreateRequestFromPdfTemplateCommand, Guid>, CreateRequestFromPdfTemplateCommandHandler>();
        services.AddScoped<IQueryHandler<GetPdfTemplatesQuery, PagedResult<PdfTemplateDto>>, GetPdfTemplatesQueryHandler>();
        services.AddScoped<IQueryHandler<GetPdfTemplateByIdQuery, PdfTemplateDetailDto>, GetPdfTemplateByIdQueryHandler>();

        // GDPR — consent + DSAR + export
        services.AddScoped<ICommandHandler<RecordConsentCommand>, RecordConsentCommandHandler>();
        services.AddScoped<ICommandHandler<SubmitDsarCommand, Guid>, SubmitDsarCommandHandler>();
        services.AddScoped<IQueryHandler<ExportUserDataQuery, UserDataExportDto>, ExportUserDataQueryHandler>();

        // Current user service (needs HttpContextAccessor)
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddHttpContextAccessor();

        return services;
    }
}
