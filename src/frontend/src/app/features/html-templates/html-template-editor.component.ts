import { Component, HostListener, OnInit, SecurityContext, computed, inject, signal, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ClausePickerDialogComponent } from '../clauses/clause-picker-dialog.component';
import { ConditionDialogComponent } from './condition-dialog.component';
import { HtmlTemplateService } from '../../core/services/html-template.service';
import { WorkflowPresetService } from '../../core/services/workflow-preset.service';
import { WorkflowPresetDto } from '../../core/models/workflow-preset.models';
import { UserService } from '../../core/services/user.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { HtmlTemplateFieldInput, HtmlFieldType, HtmlFieldRole, TemplateReviewResult } from '../../core/models/html-template.models';
import { TemplateFinding } from '../../core/models/html-template.models';
import { WorkflowType } from '../../core/models/document.models';
import { UserDto, RequestType } from '../../core/models';
import { formatSla } from '../../core/utils/sla';
import { EditHistory } from '../../core/utils/edit-history';
import { WorkflowTypeToggleComponent } from '../../shared/workflow-type-toggle/workflow-type-toggle.component';
import { ApprovalPathSummaryComponent } from '../../shared/approval-path-summary/approval-path-summary.component';
import { buildApprovalPath } from '../pdf-templates/approval-path.util';
import { lintTemplate } from './template-lint';

const WORKFLOW_TYPES: { value: WorkflowType; labelKey: string }[] = [
  { value: 'Sequential', labelKey: 'designer.workflow.sequential' },
  { value: 'Parallel', labelKey: 'designer.workflow.parallel' },
  { value: 'Mixed', labelKey: 'designer.workflow.mixed' },
];

const FIELD_TYPES: { value: HtmlFieldType; labelKey: string; icon: string }[] = [
  { value: 'Text', labelKey: 'htmlTemplateEditor.fieldType.Text', icon: 'text_fields' },
  { value: 'Textarea', labelKey: 'htmlTemplateEditor.fieldType.Textarea', icon: 'notes' },
  { value: 'Date', labelKey: 'htmlTemplateEditor.fieldType.Date', icon: 'calendar_today' },
  { value: 'Number', labelKey: 'htmlTemplateEditor.fieldType.Number', icon: 'tag' },
  { value: 'Checkbox', labelKey: 'htmlTemplateEditor.fieldType.Checkbox', icon: 'check_box' },
  { value: 'Dropdown', labelKey: 'htmlTemplateEditor.fieldType.Dropdown', icon: 'arrow_drop_down_circle' },
  { value: 'Signature', labelKey: 'htmlTemplateEditor.fieldType.Signature', icon: 'draw' },
];

const AUTO_FILL_OPTIONS = [
  { value: 'user.fullName', labelKey: 'htmlTemplateEditor.autoFillOption.fullName' },
  { value: 'user.firstName', labelKey: 'htmlTemplateEditor.autoFillOption.firstName' },
  { value: 'user.lastName', labelKey: 'htmlTemplateEditor.autoFillOption.lastName' },
  { value: 'user.email', labelKey: 'htmlTemplateEditor.autoFillOption.email' },
  { value: 'user.company', labelKey: 'htmlTemplateEditor.autoFillOption.company' },
  { value: 'user.department', labelKey: 'htmlTemplateEditor.autoFillOption.department' },
  { value: 'system.today', labelKey: 'htmlTemplateEditor.autoFillOption.today' },
  { value: 'system.now', labelKey: 'htmlTemplateEditor.autoFillOption.now' },
  { value: 'request.title', labelKey: 'htmlTemplateEditor.autoFillOption.requestTitle' },
];

/** What undo/redo restores: the contract text and the fields that go with it. */
interface EditorSnapshot { html: string; fields: HtmlTemplateFieldInput[]; }

@Component({
  selector: 'sf-html-template-editor',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatSnackBarModule, MatTooltipModule, MatDialogModule, TranslatePipe,
    MatFormFieldModule, MatInputModule, MatSelectModule, MatAutocompleteModule, MatSlideToggleModule,
    WorkflowTypeToggleComponent, ApprovalPathSummaryComponent
  ],
  template: `
    <div class="editor-page">
      <!-- Top bar -->
      <div class="top-bar">
        <a mat-icon-button routerLink="/admin/html-templates" [matTooltip]="'htmlTemplateEditor.backToTemplates' | translate">
          <mat-icon>arrow_back</mat-icon>
        </a>
        <div class="top-title">
          <input class="title-input" [(ngModel)]="name" [placeholder]="'htmlTemplateEditor.namePlaceholder' | translate" />
        </div>
        <div class="top-actions">
          <button mat-stroked-button class="ai-btn" (click)="aiPanelOpen.set(true)">
            <span class="material-symbols-outlined ai-icon">auto_awesome</span>
            {{ 'htmlTemplateEditor.generateWithAi' | translate }}
          </button>
          <button mat-stroked-button (click)="togglePreview()">
            <mat-icon>{{ previewing() ? 'edit' : 'preview' }}</mat-icon>
            {{ previewing() ? ('common.edit' | translate) : ('htmlTemplateEditor.preview' | translate) }}
          </button>
          <button mat-flat-button class="save-btn" (click)="save()" [disabled]="saving()">
            @if (saving()) { <mat-spinner diameter="18"></mat-spinner> }
            @else { <mat-icon>save</mat-icon> }
            {{ isEdit ? ('common.update' | translate) : ('common.create' | translate) }}
          </button>
        </div>
      </div>

      <!-- Settings: what the template is, how it routes, who can see whom -->
      <section class="settings">
        <button type="button" class="settings-head" (click)="settingsOpen.set(!settingsOpen())" [attr.aria-expanded]="settingsOpen()">
          <mat-icon>tune</mat-icon>
          <span class="settings-title">{{ 'htmlTemplateEditor.settings.title' | translate }}</span>
          @if (!settingsOpen()) { <span class="settings-summary">{{ settingsSummary() }}</span> }
          <mat-icon class="settings-chevron">{{ settingsOpen() ? 'expand_less' : 'expand_more' }}</mat-icon>
        </button>
        @if (settingsOpen()) {
          <div class="settings-grid">
            <div class="s-card">
              <h3><mat-icon>description</mat-icon> {{ 'htmlTemplateEditor.settings.basics' | translate }}</h3>
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ff">
                <mat-label>{{ 'common.description' | translate }}</mat-label>
                <input matInput [(ngModel)]="description" [placeholder]="'htmlTemplateEditor.descriptionPlaceholder' | translate" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ff">
                <mat-label>{{ 'htmlTemplateEditor.requestType' | translate }}</mat-label>
                <mat-select [(ngModel)]="requestTypeId">
                  <mat-option value="">{{ 'htmlTemplateEditor.noRequestType' | translate }}</mat-option>
                  @for (rt of requestTypes(); track rt.id) {
                    <mat-option [value]="rt.id">{{ rt.name }} ({{ formatSla(rt.slaHours) }})</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              @if (isEdit) {
                <mat-slide-toggle [(ngModel)]="isActive">
                  {{ isActive ? ('htmlTemplateEditor.active' | translate) : ('htmlTemplateEditor.inactive' | translate) }}
                </mat-slide-toggle>
              }
            </div>

            <div class="s-card">
              <h3><mat-icon>route</mat-icon> {{ 'htmlTemplateEditor.settings.routing' | translate }}</h3>
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="ff">
                <mat-label>{{ 'admin.workflows.pickLabel' | translate }}</mat-label>
                <mat-select [ngModel]="workflowPresetId()" (ngModelChange)="onPresetChosen($event)">
                  <mat-option value="">{{ 'admin.workflows.pickNone' | translate }}</mat-option>
                  @for (p of selectablePresets(); track p.id) {
                    <mat-option [value]="p.id">{{ p.name }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              @if (!selectedPreset()) {
                <sf-workflow-type-toggle [compact]="true" [value]="workflowType()" (valueChange)="workflowType.set($event)"></sf-workflow-type-toggle>
              }
              @if (selectedPreset(); as preset) {
                <ol class="preset-steps">
                  @for (st of preset.steps; track st.order) {
                    <li>
                      <span class="preset-badge">{{ st.order }}</span>
                      <span class="preset-who"><strong>{{ st.label || ('admin.workflows.step' | translate) }}</strong> · {{ stepWho(st) }}</span>
                      @if (st.conditionFieldKey) { <span class="preset-cond">{{ 'admin.workflows.onlyWhen' | translate }} {{ st.conditionFieldKey }}{{ st.conditionOperator ? ' ' + st.conditionOperator + ' ' + st.conditionValue : '' }}</span> }
                    </li>
                  }
                </ol>
                <p class="s-hint">{{ 'workflow.type.' + preset.workflowType | translate }} · {{ 'admin.workflows.follows' | translate }}</p>
                @for (w of presetWarnings(); track w.code) {
                  <p class="s-warn"><mat-icon>warning_amber</mat-icon> {{ 'admin.workflows.warn.' + w.code | translate: w.params }}</p>
                }
              } @else {
                <sf-approval-path-summary [path]="path()" [names]="userNames()"></sf-approval-path-summary>
              }
            </div>

            <div class="s-card">
              <h3><mat-icon>visibility_off</mat-icon> {{ 'htmlTemplateEditor.settings.privacy' | translate }}</h3>
              <mat-slide-toggle [checked]="hideRecipientIdentities()" (change)="hideRecipientIdentities.set($event.checked)">
                {{ 'designer.anon.hideIdentities' | translate }}
              </mat-slide-toggle>
              <p class="s-hint">{{ 'designer.anon.hideIdentitiesHint' | translate }}</p>
            </div>
          </div>
        }
      </section>

      <!-- Editor body -->
      <div class="editor-body">
        <!-- HTML editor / preview -->
        <div class="html-panel">
          <div class="panel-header">
            <span class="panel-label">{{ 'htmlTemplateEditor.htmlContentLabel' | translate }}</span>
            <span class="hist-btns">
              <button type="button" class="hist-btn" [disabled]="!history.canUndo()" (click)="history.undo()"
                      [matTooltip]="('htmlTemplateEditor.undo' | translate) + ' (Ctrl+Z)'" [attr.aria-label]="'htmlTemplateEditor.undo' | translate">
                <mat-icon>undo</mat-icon>
              </button>
              <button type="button" class="hist-btn" [disabled]="!history.canRedo()" (click)="history.redo()"
                      [matTooltip]="('htmlTemplateEditor.redo' | translate) + ' (Ctrl+Shift+Z)'" [attr.aria-label]="'htmlTemplateEditor.redo' | translate">
                <mat-icon>redo</mat-icon>
              </button>
            </span>
            <button class="clause-lib-btn" (click)="openClausePicker()"
                    [matTooltip]="'clauses.picker.buttonTooltip' | translate">
              <mat-icon>menu_book</mat-icon> {{ 'clauses.picker.button' | translate }}
            </button>
            <button class="clause-lib-btn" (click)="wrapSelectionInCondition()"
                    [matTooltip]="'htmlTemplateEditor.conditions.buttonTooltip' | translate">
              <mat-icon>rule</mat-icon> {{ 'htmlTemplateEditor.conditions.button' | translate }}
            </button>
            <button class="clause-lib-btn issues-chip" [class.has-errors]="lintErrors() > 0" (click)="issuesOpen.set(!issuesOpen())"
                    [matTooltip]="'htmlTemplateEditor.lint.title' | translate">
              <mat-icon>{{ lintErrors() > 0 ? 'error_outline' : (lint().length ? 'warning_amber' : 'check_circle_outline') }}</mat-icon>
              {{ lint().length }}
            </button>
            <button class="clause-lib-btn review-btn" (click)="runReview()" [disabled]="reviewing()"
                    [matTooltip]="'htmlTemplateEditor.review.tooltip' | translate">
              @if (reviewing()) { <mat-spinner diameter="14"></mat-spinner> } @else { <mat-icon>gavel</mat-icon> }
              {{ 'htmlTemplateEditor.review.button' | translate }}
            </button>
            <span class="panel-hint" [innerHTML]="'htmlTemplateEditor.panelHint' | translate: { example: curlyExample }"></span>
          </div>
          @if (issuesOpen()) {
            <div class="issues-panel">
              <div class="issues-section">
                <h4>{{ 'htmlTemplateEditor.lint.title' | translate }}</h4>
                @if (lint().length === 0) {
                  <p class="issue ok"><mat-icon>check_circle</mat-icon> {{ 'htmlTemplateEditor.lint.none' | translate }}</p>
                }
                @for (i of lint(); track $index) {
                  <p class="issue" [class.error]="i.severity === 'error'">
                    <mat-icon>{{ i.severity === 'error' ? 'error_outline' : 'warning_amber' }}</mat-icon>
                    {{ 'htmlTemplateEditor.lint.' + i.code | translate: i.params }}
                  </p>
                }
              </div>
              <div class="issues-section">
                <h4>{{ 'htmlTemplateEditor.review.title' | translate }}
                  @if (review() && reviewStale()) { <span class="stale">{{ 'htmlTemplateEditor.review.stale' | translate }}</span> }
                </h4>
                @if (reviewing()) {
                  <p class="issue"><mat-spinner diameter="14"></mat-spinner> {{ 'htmlTemplateEditor.review.running' | translate }}</p>
                }
                @if (reviewError()) {
                  <p class="issue error"><mat-icon>error_outline</mat-icon> {{ reviewError() }}</p>
                }
                @if (!review() && !reviewing() && !reviewError()) {
                  <p class="issue muted">{{ 'htmlTemplateEditor.review.intro' | translate }}</p>
                }
                @if (review(); as r) {
                  <p class="issue muted">{{ 'htmlTemplateEditor.review.disclaimer' | translate }}</p>
                  @if (r.findings.length === 0) {
                    <p class="issue ok"><mat-icon>check_circle</mat-icon> {{ 'htmlTemplateEditor.review.none' | translate }}</p>
                  }
                  @for (f of r.findings; track $index) {
                    <div class="finding" [attr.data-severity]="f.severity">
                      <div class="finding-head">
                        <strong>{{ f.clauseType }}</strong>
                        <span class="sev">{{ 'htmlTemplateEditor.review.severity.' + f.severity | translate }}</span>
                      </div>
                      <p>{{ f.explanation }}</p>
                      @if (f.recommendedAction) { <p class="rec">{{ f.recommendedAction }}</p> }
                      @if (f.proposedLanguage) {
                        <button type="button" class="clause-lib-btn" (click)="insertProposed(f)">
                          <mat-icon>add_circle_outline</mat-icon> {{ 'htmlTemplateEditor.review.insert' | translate }}
                        </button>
                      }
                    </div>
                  }
                  @for (l of r.limitations; track $index) { <p class="issue muted">{{ l }}</p> }
                }
              </div>
            </div>
          }
          @if (previewing()) {
            <div class="preview-pane contract-doc" [innerHTML]="previewHtml"></div>
          } @else {
            <textarea #htmlEditor class="html-textarea" [ngModel]="htmlContent" (ngModelChange)="onHtmlTyped($event)"
              [placeholder]="'htmlTemplateEditor.htmlPlaceholder' | translate" (paste)="onHtmlPaste()"></textarea>
          }
        </div>

        <!-- Field sidebar -->
        <div class="field-sidebar">
          <div class="panel-header">
            <span class="panel-label">{{ 'htmlTemplateEditor.fieldsLabel' | translate }}</span>
            <button class="add-field-btn" (click)="addField()">
              <mat-icon>add</mat-icon>
            </button>
          </div>

          @if (fields().length === 0) {
            <div class="fields-empty">
              <p>{{ 'htmlTemplateEditor.noFieldsYet' | translate }}</p>
              <p>{{ 'htmlTemplateEditor.noFieldsHint' | translate }}</p>
            </div>
          }

          <div class="fields-list">
            @for (f of fields(); track f.fieldKey; let i = $index) {
              <div class="field-card" [class.expanded]="expandedIdx() === i">
                <div class="field-card-header" (click)="toggleField(i)">
                  <span class="field-type-icon material-symbols-outlined">{{ iconFor(f.type) }}</span>
                  <div class="field-card-title">
                    <span class="field-label-text">{{ f.label || ('htmlTemplateEditor.noLabelFallback' | translate) }}</span>
                    <span class="field-key-text">{{ toPlaceholder(f.fieldKey) }}</span>
                  </div>
                  <div class="field-card-badges">
                    <span class="badge-role" [class.approver]="f.role === 'Approver'">{{ ('htmlTemplateEditor.role.' + f.role) | translate }}</span>
                  </div>
                  <div class="field-card-btns">
                    <button class="insert-btn" (click)="insertPlaceholder(f.fieldKey, $event)" [matTooltip]="'htmlTemplateEditor.insertIntoHtml' | translate">
                      <mat-icon>add_circle_outline</mat-icon>
                    </button>
                    <button class="remove-btn" (click)="removeField(i, $event)" [matTooltip]="'htmlTemplateEditor.remove' | translate">
                      <mat-icon>delete_outline</mat-icon>
                    </button>
                    <mat-icon class="chevron">{{ expandedIdx() === i ? 'expand_less' : 'expand_more' }}</mat-icon>
                  </div>
                </div>

                @if (expandedIdx() === i) {
                  <div class="field-card-body" (input)="onFieldsEdited()" (change)="onFieldsEdited()">
                    <div class="fc-row">
                      <label>{{ 'htmlTemplateEditor.keyLabel' | translate }}</label>
                      <input [(ngModel)]="f.fieldKey" class="fc-input" [placeholder]="'htmlTemplateEditor.keyPlaceholder' | translate" (change)="sanitizeKey(f)" />
                    </div>
                    <div class="fc-row">
                      <label>{{ 'htmlTemplateEditor.labelLabel' | translate }}</label>
                      <input [(ngModel)]="f.label" class="fc-input" [placeholder]="'htmlTemplateEditor.labelPlaceholder' | translate" />
                    </div>
                    <div class="fc-row">
                      <label>{{ 'htmlTemplateEditor.typeLabel' | translate }}</label>
                      <select [(ngModel)]="f.type" class="fc-select">
                        @for (t of fieldTypes; track t.value) {
                          <option [value]="t.value">{{ t.labelKey | translate }}</option>
                        }
                      </select>
                    </div>
                    <div class="fc-row">
                      <label>{{ 'htmlTemplateEditor.roleLabel' | translate }}</label>
                      <select [(ngModel)]="f.role" class="fc-select">
                        <option value="Initiator">{{ 'htmlTemplateEditor.roleInitiatorOption' | translate }}</option>
                        <option value="Approver">{{ 'htmlTemplateEditor.roleApproverOption' | translate }}</option>
                      </select>
                    </div>
                    <div class="fc-row fc-check">
                      <label>{{ 'common.required' | translate }}</label>
                      <input type="checkbox" [(ngModel)]="f.isRequired" />
                    </div>
                    @if (f.type === 'Dropdown') {
                      <div class="fc-row">
                        <label>{{ 'htmlTemplateEditor.optionsLabel' | translate }}</label>
                        <input class="fc-input" [ngModel]="optionsText(f)" (ngModelChange)="setOptions(f, $event)"
                               [placeholder]="'htmlTemplateEditor.optionsPlaceholder' | translate" />
                      </div>
                    }
                    @if (f.role === 'Initiator' && f.type !== 'Signature' && !f.autoFillFrom) {
                      <div class="fc-row">
                        <label>{{ 'htmlTemplateEditor.defaultValueLabel' | translate }} <span class="fc-hint">{{ 'htmlTemplateEditor.defaultValueHint' | translate }}</span></label>
                        @switch (f.type) {
                          @case ('Checkbox') {
                            <select class="fc-select" [ngModel]="f.defaultValue ?? ''" (ngModelChange)="f.defaultValue = $event">
                              <option value="">{{ 'htmlTemplateEditor.defaultNone' | translate }}</option>
                              <option value="true">{{ 'common.yes' | translate }}</option>
                              <option value="false">{{ 'common.no' | translate }}</option>
                            </select>
                          }
                          @case ('Dropdown') {
                            <select class="fc-select" [ngModel]="f.defaultValue ?? ''" (ngModelChange)="f.defaultValue = $event">
                              <option value="">{{ 'htmlTemplateEditor.defaultNone' | translate }}</option>
                              @for (o of optionsList(f); track o) { <option [value]="o">{{ o }}</option> }
                            </select>
                          }
                          @case ('Date') {
                            <input class="fc-input" type="date" [ngModel]="f.defaultValue ?? ''" (ngModelChange)="f.defaultValue = $event" />
                          }
                          @case ('Number') {
                            <input class="fc-input" type="number" [ngModel]="f.defaultValue ?? ''" (ngModelChange)="f.defaultValue = ('' + ($event ?? ''))" />
                          }
                          @default {
                            <input class="fc-input" type="text" [ngModel]="f.defaultValue ?? ''" (ngModelChange)="f.defaultValue = $event" />
                          }
                        }
                      </div>
                    }
                    @if (f.role === 'Initiator' && f.type !== 'Signature') {
                      <div class="fc-row">
                        <label>{{ 'htmlTemplateEditor.autoFillLabel' | translate }}</label>
                        <select [(ngModel)]="f.autoFillFrom" class="fc-select">
                          <option value="">{{ 'htmlTemplateEditor.autoFillNone' | translate }}</option>
                          @for (a of autoFillOptions; track a.value) {
                            <option [value]="a.value">{{ a.labelKey | translate }}</option>
                          }
                        </select>
                      </div>
                    }
                    @if (f.role === 'Approver' && f.type === 'Signature') {
                      @if (!isPlaced(f.fieldKey)) {
                        <div class="fc-warn">
                          <span>{{ 'htmlTemplateEditor.signatureNotPlaced' | translate }}</span>
                          <button type="button" class="insert-btn" (click)="insertPlaceholder(f.fieldKey, $event)">
                            {{ 'htmlTemplateEditor.insertIntoHtml' | translate }}
                          </button>
                        </div>
                      }
                      @if (workflowType() === 'Mixed') {
                        <div class="fc-row">
                          <label>{{ 'htmlTemplateEditor.stage' | translate }} <span class="fc-hint">{{ 'htmlTemplateEditor.stageHint' | translate }}</span></label>
                          <input type="number" min="1" [(ngModel)]="f.filledByStep" class="fc-input" />
                        </div>
                      }
                    }
                  </div>
                }
              </div>
            }
          </div>
        </div>
      </div>
      <!-- AI Generate Panel (slide-in overlay) -->
      @if (aiPanelOpen()) {
        <div class="ai-overlay" (click)="closeAiPanel($event)">
          <div class="ai-panel" (click)="$event.stopPropagation()">
            <div class="ai-panel-header">
              <div class="ai-panel-title">
                <span class="material-symbols-outlined">auto_awesome</span>
                <span>{{ 'htmlTemplateEditor.generateWithAi' | translate }}</span>
              </div>
              <button mat-icon-button (click)="aiPanelOpen.set(false)">
                <mat-icon>close</mat-icon>
              </button>
            </div>

            <div class="ai-panel-body">
              <p class="ai-hint">{{ 'htmlTemplateEditor.aiHint' | translate }}</p>
              <textarea class="ai-prompt-textarea" [(ngModel)]="aiPrompt"
                [placeholder]="'htmlTemplateEditor.aiPromptPlaceholder' | translate"
                rows="10"></textarea>
            </div>

            <div class="ai-panel-footer">
              <span class="ai-model-badge">
                <span class="material-symbols-outlined">smart_toy</span> Harvey
              </span>
              <div class="ai-footer-actions">
                <button mat-stroked-button (click)="aiPanelOpen.set(false)">{{ 'common.cancel' | translate }}</button>
                <button mat-flat-button class="ai-generate-btn" (click)="generateWithAi()" [disabled]="aiGenerating() || !aiPrompt.trim()">
                  @if (aiGenerating()) {
                    <mat-spinner diameter="16"></mat-spinner> {{ 'htmlTemplateEditor.generating' | translate }}
                  } @else {
                    <span class="material-symbols-outlined">auto_awesome</span> {{ 'htmlTemplateEditor.generate' | translate }}
                  }
                </button>
              </div>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .fc-warn { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin: 6px 0; padding: 6px 10px; border-radius: 8px; background: #fef7e0; color: #b06000; font-size: 12px; }
    :host { display: block; height: 100%; }
    .editor-page { display: flex; flex-direction: column; height: calc(100vh - 64px); gap: 0; }

    /* Top bar */
    .top-bar { display: flex; align-items: center; gap: 12px; padding: 12px 0 12px; border-bottom: 1px solid #e8edf2; background: #f0f4f9; }
    .top-title { flex: 1; }
    .title-input { font-size: 18px; font-weight: 500; font-family: 'Google Sans', sans-serif; border: none; background: transparent; outline: none; width: 100%; color: #1f1f1f; }
    .title-input::placeholder { color: #9aa0a6; }
    .top-actions { display: flex; gap: 8px; align-items: center; }
    .save-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 20px !important; display: flex; align-items: center; gap: 6px; }

    /* Settings */
    .settings { border-bottom: 1px solid #e8edf2; padding: 8px 0 12px; }
    .settings-head { display: flex; align-items: center; gap: 8px; width: 100%; border: none; background: transparent; padding: 4px 0; cursor: pointer; font-family: inherit; text-align: left; color: #1f1f1f; }
    .settings-head mat-icon { color: #5f6368; font-size: 20px; width: 20px; height: 20px; }
    .settings-title { font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; color: #5f6368; }
    .settings-summary { flex: 1; min-width: 0; font-size: 13px; color: #5f6368; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .settings-chevron { margin-left: auto; }
    .settings-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 12px; margin-top: 8px; max-height: 38vh; overflow-y: auto; padding: 2px; }
    .s-card { background: #fff; border: 1px solid #c7d0dc; border-radius: 12px; padding: 12px 14px 14px; display: flex; flex-direction: column; gap: 10px; min-width: 0; }
    .s-card h3 { display: flex; align-items: center; gap: 6px; margin: 0; font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; color: #0b57d0; }
    .s-card h3 mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .s-card .ff { width: 100%; }
    .s-card ::ng-deep .mat-mdc-form-field-infix { min-height: 44px; padding-top: 10px !important; padding-bottom: 10px !important; font-size: 14px; }
    .s-hint { margin: 0; font-size: 12px; color: #5f6368; line-height: 1.5; }
    .opt-sub { color: #5f6368; font-size: 12px; margin-left: 6px; }
    .preset-steps { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .preset-steps li { display: flex; align-items: baseline; flex-wrap: wrap; gap: 2px 8px; font-size: 13px; }
    .preset-badge { flex: none; width: 22px; height: 22px; border-radius: 50%; background: #0b57d0; color: #fff; font-size: 12px; font-weight: 600; display: inline-flex; align-items: center; justify-content: center; }
    .preset-cond { width: 100%; padding-left: 30px; font-size: 12px; color: #b06000; }
    .s-warn { display: flex; align-items: flex-start; gap: 6px; margin: 0; font-size: 12px; line-height: 1.4; color: #b06000; }
    .s-warn mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; }

    /* Undo / redo, lint and review */
    .hist-btns { display: inline-flex; gap: 2px; margin-right: 4px; }
    .hist-btn { width: 30px; height: 30px; border: none; background: transparent; border-radius: 8px; display: inline-flex; align-items: center; justify-content: center; cursor: pointer; color: #444746; }
    .hist-btn:hover:not([disabled]) { background: #e8eaed; }
    .hist-btn[disabled] { color: #bdc1c6; cursor: default; }
    .hist-btn mat-icon { font-size: 19px; width: 19px; height: 19px; }
    .panel-header { gap: 8px; flex-wrap: wrap; justify-content: flex-start !important; }
    .panel-header .panel-hint { margin-left: auto; }
    .issues-chip { background: #e6f4ea; color: #137333; border-color: #ceead6; }
    .issues-chip.has-errors { background: #fce8e6; color: #c5221f; border-color: #f6c6c2; }
    .review-btn[disabled] { opacity: 0.7; cursor: default; }
    .issues-panel { max-height: 40%; overflow-y: auto; border-bottom: 1px solid #e8edf2; background: #fbfcfe; padding: 10px 16px 12px; display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 16px; }
    .issues-section h4 { margin: 0 0 6px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em; color: #5f6368; }
    .issue { display: flex; align-items: flex-start; gap: 6px; margin: 4px 0; font-size: 12.5px; line-height: 1.45; color: #b06000; }
    .issue mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; margin-top: 1px; }
    .issue.error { color: #c5221f; }
    .issue.ok { color: #137333; }
    .issue.muted { color: #5f6368; }
    .stale { margin-left: 6px; padding: 1px 6px; border-radius: 8px; background: #fef7e0; color: #b06000; font-size: 10px; text-transform: none; letter-spacing: 0; }
    .finding { border: 1px solid #dadce0; border-left-width: 4px; border-radius: 8px; padding: 8px 10px; margin: 6px 0; background: #fff; font-size: 12.5px; line-height: 1.5; }
    .finding p { margin: 4px 0; }
    .finding .rec { color: #0b57d0; }
    .finding-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .finding .sev { font-size: 10px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em; }
    .finding[data-severity="Critical"] { border-left-color: #c5221f; } .finding[data-severity="Critical"] .sev { color: #c5221f; }
    .finding[data-severity="High"] { border-left-color: #e8710a; } .finding[data-severity="High"] .sev { color: #e8710a; }
    .finding[data-severity="Medium"] { border-left-color: #f9ab00; } .finding[data-severity="Medium"] .sev { color: #b06000; }
    .finding[data-severity="Low"] { border-left-color: #5f6368; } .finding[data-severity="Low"] .sev { color: #5f6368; }

    .fc-hint { text-transform: none; font-weight: 400; color: #9aa0a6; letter-spacing: normal; }

    /* Editor body */
    .editor-body { flex: 1; display: flex; gap: 16px; overflow: hidden; padding: 16px 0 0; min-height: 0; }

    .html-panel { flex: 1; display: flex; flex-direction: column; border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; overflow: hidden; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .panel-header { display: flex; align-items: center; justify-content: space-between; padding: 10px 16px; border-bottom: 1px solid #e8edf2; background: #f8faff; }
    .panel-label { font-size: 13px; font-weight: 500; color: #1f1f1f; }
    .panel-hint { font-size: 12px; color: #5f6368; }
    .clause-lib-btn { display: inline-flex; align-items: center; gap: 5px; background: #f3e8fd; color: #7627bb; border: 1px solid #e4d0f7; border-radius: 8px; padding: 4px 10px; font-size: 12px; font-weight: 600; font-family: inherit; cursor: pointer; }
    .clause-lib-btn:hover { background: #ecdcfb; }
    .clause-lib-btn mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .panel-hint code { background: #e8f0fe; color: #0b57d0; padding: 1px 5px; border-radius: 4px; font-size: 11px; }

    .html-textarea { flex: 1; width: 100%; border: none; outline: none; resize: none; font-family: 'Consolas', 'Courier New', monospace; font-size: 13px; line-height: 1.6; padding: 16px; color: #1f1f1f; box-sizing: border-box; background: #fff; }
    .preview-pane { flex: 1; overflow: auto; padding: 24px 32px; }

    /* Field sidebar */
    .field-sidebar { width: 320px; flex-shrink: 0; display: flex; flex-direction: column; border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; overflow: hidden; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .field-sidebar .panel-header { justify-content: space-between; }
    .add-field-btn { width: 28px; height: 28px; border-radius: 50%; border: none; background: #0b57d0; color: #fff; cursor: pointer; display: flex; align-items: center; justify-content: center; }
    .add-field-btn mat-icon { font-size: 18px; width: 18px; height: 18px; }

    .fields-empty { padding: 24px 16px; text-align: center; color: #9aa0a6; font-size: 13px; }
    .fields-empty p { margin: 4px 0; }

    .fields-list { flex: 1; overflow-y: auto; }
    .field-card { border-bottom: 1px solid #e8edf2; }
    .field-card:last-child { border-bottom: none; }

    .field-card-header { display: flex; align-items: center; gap: 8px; padding: 10px 12px; cursor: pointer; gap: 8px; }
    .field-card-header:hover { background: #f8faff; }
    .field-type-icon { font-size: 18px; color: #0b57d0; flex-shrink: 0; }
    .field-card-title { flex: 1; min-width: 0; }
    .field-label-text { display: block; font-size: 13px; font-weight: 500; color: #1f1f1f; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .field-key-text { font-size: 11px; color: #0b57d0; font-family: monospace; }

    .field-card-badges { display: flex; gap: 4px; }
    .badge-role { font-size: 10px; font-weight: 500; padding: 2px 6px; border-radius: 10px; background: #e8f0fe; color: #0b57d0; white-space: nowrap; }
    .badge-role.approver { background: #fce8e6; color: #c5221f; }

    .field-card-btns { display: flex; align-items: center; gap: 2px; }
    .insert-btn, .remove-btn { background: none; border: none; cursor: pointer; color: #5f6368; padding: 4px; border-radius: 4px; display: flex; align-items: center; }
    .insert-btn:hover { color: #0b57d0; }
    .remove-btn:hover { color: #c5221f; }
    .insert-btn mat-icon, .remove-btn mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .chevron { font-size: 18px; color: #9aa0a6; }

    .field-card-body { padding: 12px; background: #f8faff; border-top: 1px solid #e8edf2; display: flex; flex-direction: column; gap: 8px; }
    .fc-row { display: flex; flex-direction: column; gap: 3px; }
    .fc-row label { font-size: 11px; font-weight: 500; color: #5f6368; text-transform: uppercase; letter-spacing: 0.4px; }
    .fc-input, .fc-select { height: 32px; padding: 0 10px; border-radius: 6px; border: 1px solid #c7d0dc; background: #fff; font-size: 13px; font-family: inherit; outline: none; width: 100%; box-sizing: border-box; }
    .fc-input:focus, .fc-select:focus { border-color: #0b57d0; }
    .fc-check { flex-direction: row; align-items: center; gap: 8px; }

    /* AI Generate button */
    .ai-btn { border-color: #7c4dff !important; color: #7c4dff !important; border-radius: 20px !important; display: flex; align-items: center; gap: 6px; }
    .ai-icon { font-size: 18px; }

    /* AI overlay */
    .ai-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.4); z-index: 1000; display: flex; align-items: flex-start; justify-content: flex-end; }
    .ai-panel { width: 480px; height: 100%; background: #fff; display: flex; flex-direction: column; box-shadow: -4px 0 24px rgba(0,0,0,0.15); animation: slideIn 0.2s ease; }
    @keyframes slideIn { from { transform: translateX(100%); } to { transform: none; } }

    .ai-panel-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-bottom: 1px solid #e8edf2; }
    .ai-panel-title { display: flex; align-items: center; gap: 10px; font-size: 15px; font-weight: 500; color: #1f1f1f; }
    .ai-panel-title .material-symbols-outlined { font-size: 20px; color: #7c4dff; }

    .ai-panel-body { flex: 1; padding: 20px; display: flex; flex-direction: column; gap: 12px; overflow-y: auto; }
    .ai-hint { font-size: 13px; color: #5f6368; margin: 0; line-height: 1.5; }
    .ai-prompt-textarea { width: 100%; flex: 1; min-height: 200px; padding: 12px 14px; border-radius: 8px; border: 1px solid #c7d0dc; font-size: 14px; font-family: inherit; line-height: 1.6; resize: vertical; outline: none; box-sizing: border-box; }
    .ai-prompt-textarea:focus { border-color: #7c4dff; box-shadow: 0 0 0 2px rgba(124,77,255,0.15); }

    .ai-panel-footer { display: flex; align-items: center; justify-content: space-between; padding: 16px 20px; border-top: 1px solid #e8edf2; }
    .ai-model-badge { display: flex; align-items: center; gap: 6px; font-size: 12px; color: #9aa0a6; }
    .ai-model-badge .material-symbols-outlined { font-size: 16px; }
    .ai-footer-actions { display: flex; gap: 8px; }
    .ai-generate-btn { background: #7c4dff !important; color: #fff !important; border-radius: 20px !important; display: flex; align-items: center; gap: 6px; }
    .ai-generate-btn .material-symbols-outlined { font-size: 16px; }
  `]
})
export class HtmlTemplateEditorComponent implements OnInit {
  @ViewChild('htmlEditor') htmlEditorRef?: ElementRef<HTMLTextAreaElement>;

  private readonly svc = inject(HtmlTemplateService);
  private readonly userSvc = inject(UserService);
  private readonly requestTypeSvc = inject(RequestTypeService);
  private readonly presetSvc = inject(WorkflowPresetService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly dialog = inject(MatDialog);

  readonly fieldTypes = FIELD_TYPES;
  readonly autoFillOptions = AUTO_FILL_OPTIONS;
  readonly workflowTypes = WORKFLOW_TYPES;
  readonly curlyExample = '{{fieldKey}}';

  isEdit = false;
  templateId: string | null = null;
  saving = signal(false);
  previewing = signal(false);
  users = signal<UserDto[]>([]);
  requestTypes = signal<RequestType[]>([]);
  fields = signal<HtmlTemplateFieldInput[]>([]);
  expandedIdx = signal<number | null>(null);
  workflowType = signal<WorkflowType>('Sequential');
  hideRecipientIdentities = signal(false);
  /** The reusable approval workflow this template follows ('' = none). */
  workflowPresetId = signal('');
  presets = signal<WorkflowPresetDto[]>([]);
  /** What the approver box shows: the text being typed, or the chosen user. */
  settingsOpen = signal(true);
  readonly formatSla = formatSla;

  name = '';
  description = '';
  htmlContent = '';
  requestTypeId = '';
  isActive = true;

  aiPanelOpen = signal(false);
  aiGenerating = signal(false);
  aiPrompt = '';

  /** Bumped whenever the text or the fields change, so what is derived from them (path, lint, stale review) follows. */
  private readonly version = signal(0);
  issuesOpen = signal(false);
  review = signal<TemplateReviewResult | null>(null);
  reviewing = signal(false);
  reviewError = signal('');
  private readonly reviewedVersion = signal(-1);
  readonly reviewStale = computed(() => this.reviewedVersion() !== this.version());

  readonly lint = computed(() => { this.version(); return lintTemplate(this.htmlContent, this.fields()); });
  readonly lintErrors = computed(() => this.lint().filter(i => i.severity === 'error').length);

  readonly userNames = computed(() => Object.fromEntries(this.users().map(u => [u.id, u.fullName])));

  /** The approval path the server will build from the fields and the workflow type. */
  readonly path = computed(() => {
    this.version();
    return buildApprovalPath(
      this.fields().map(f => ({
        tempId: f.fieldKey, label: f.label, type: f.type, fillRole: f.role,
        filledByStep: f.filledByStep || 1
      })),
      this.workflowType());
  });

  readonly selectedPreset = computed(() => this.presets().find(p => p.id === this.workflowPresetId()) ?? null);

  /** Active workflows, plus the one this template already follows even if it has since been switched off. */
  readonly selectablePresets = computed(() => this.presets().filter(p => p.isActive || p.id === this.workflowPresetId()));

  private readonly approverSignatureCount = computed(() => {
    this.version();
    return this.fields().filter(f => f.role === 'Approver' && f.type === 'Signature').length;
  });

  /** Signature fields past the workflow's last step still need someone: the default approver, or the requester's choice. */

  /** Where the workflow and the template's signature fields do not line up. */
  readonly presetWarnings = computed(() => {
    const preset = this.selectedPreset();
    if (!preset) return [];
    const steps = preset.steps.length;
    const sigs = this.approverSignatureCount();
    const warnings: { code: string; params: Record<string, string> }[] = [];
    if (steps > sigs) warnings.push({ code: 'stepsIgnored', params: { steps: String(steps), fields: String(sigs), extra: String(steps - sigs) } });
    if (sigs > steps) warnings.push({ code: 'fieldsUncovered', params: { steps: String(steps), fields: String(sigs), extra: String(sigs - steps) } });
    if (!preset.isActive) warnings.push({ code: 'inactive', params: {} });
    return warnings;
  });

  /** One line for the collapsed settings bar. */
  readonly settingsSummary = computed(() => {
    const preset = this.selectedPreset();
    const parts = preset ? [preset.name] : [this.translate.instant('workflow.type.' + this.workflowType())];
    const type = this.requestTypes().find(t => t.id === this.requestTypeId);
    if (type) parts.push(`${type.name} (${formatSla(type.slaHours)})`);
    return parts.join(' · ');
  });

  /**
   * Undo/redo over the text and the field list together: AI generation and clause insertion change both, and a step
   * back must not leave fields that no longer match the text.
   */
  readonly history = new EditHistory<EditorSnapshot>(
    { html: '', fields: [] },
    () => ({ html: this.htmlContent, fields: structuredClone(this.fields()) }),
    snap => { this.htmlContent = snap.html; this.fields.set(structuredClone(snap.fields)); this.expandedIdx.set(null); this.bump(); },
    (a, b) => a.html === b.html && JSON.stringify(a.fields) === JSON.stringify(b.fields));

  private readonly sanitizer = inject(DomSanitizer);
  // Sanitised like the signing page, so the preview shows what a signer will see (and AI markup cannot run script).
  get previewHtml(): string { return this.sanitizer.sanitize(SecurityContext.HTML, this.htmlContent) ?? ''; }

  ngOnInit() {
    this.templateId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.templateId;

    this.userSvc.getUsers({ pageSize: 200 }).subscribe(r => this.users.set(r.items));
    this.requestTypeSvc.getActive().subscribe(types => this.requestTypes.set(types));
    this.presetSvc.getAll().subscribe({ next: p => this.presets.set(p), error: () => this.presets.set([]) });

    if (this.isEdit && this.templateId) {
      this.svc.getById(this.templateId).subscribe(t => {
        this.name = t.name;
        this.description = t.description ?? '';
        this.htmlContent = t.htmlContent;
        this.workflowPresetId.set(t.workflowPreset?.id ?? '');
        this.settingsOpen.set(false);
        this.requestTypeId = t.requestTypeId ?? '';
        this.isActive = t.isActive;
        this.workflowType.set(t.workflowType);
        this.hideRecipientIdentities.set(t.hideRecipientIdentities);
        this.fields.set(t.fields.map(f => ({
          fieldKey: f.fieldKey,
          label: f.label,
          type: f.type,
          role: f.role,
          isRequired: f.isRequired,
          order: f.order,
          autoFillFrom: f.autoFillFrom ?? '',
          optionsJson: f.optionsJson ?? '',
          defaultValue: f.defaultValue ?? '',
          filledByStep: f.filledByStep ?? 1
        })));
        this.history.reset({ html: this.htmlContent, fields: structuredClone(this.fields()) });
        this.bump();
      });
    }
  }

  togglePreview() { this.previewing.update(v => !v); }

  private bump(): void { this.version.update(v => v + 1); }

  /** Typing in the text box: a burst of keystrokes is one undo step. */
  onHtmlTyped(value: string): void {
    this.htmlContent = value;
    this.bump();
    this.history.typed();
  }

  /** An edit inside a field card (typing, a select, a checkbox). */
  onFieldsEdited(): void {
    this.bump();
    this.history.typed();
  }

  /** Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y, except where the browser's own undo belongs to a small input. */
  @HostListener('document:keydown', ['$event'])
  onKeydown(e: KeyboardEvent): void {
    if (!(e.ctrlKey || e.metaKey) || e.altKey) return;
    const key = e.key.toLowerCase();
    const redo = (key === 'z' && e.shiftKey) || key === 'y';
    if (!(key === 'z' && !e.shiftKey) && !redo) return;

    const target = e.target as HTMLElement | null;
    const inOtherField = !!target && (
      target.isContentEditable ||
      target.closest('input, select') !== null ||
      (target.tagName === 'TEXTAREA' && target !== this.htmlEditorRef?.nativeElement));
    if (inOtherField) return;

    e.preventDefault();
    if (redo) this.history.redo(); else this.history.undo();
  }

  // ---- reusable workflow

  /** Picking a workflow hands routing over to it, so the workflow type follows the workflow. */
  onPresetChosen(id: string): void {
    this.workflowPresetId.set(id);
    const preset = this.presets().find(p => p.id === id);
    if (preset) this.workflowType.set(preset.workflowType);
    this.bump();
  }

  stepWho(s: { kind: string; userName?: string; roleName?: string; department?: string }): string {
    switch (s.kind) {
      case 'User': return s.userName ?? '?';
      case 'Role': return `${this.translate.instant('admin.workflows.kind.Role')}: ${s.roleName}`;
      default: return `${this.translate.instant('admin.workflows.kind.Department')}: ${s.department}`;
    }
  }

  // ---- default approver box

  displayUser = (u: UserDto | string | null): string => (u && typeof u !== 'string' ? u.fullName : (u ?? ''));


  // ---- review

  /** Legal review of the template as it stands now, edits not yet saved included. */
  runReview(): void {
    if (!this.htmlContent.trim()) {
      this.snack.open(this.translate.instant('htmlTemplateEditor.htmlContentRequired'), 'OK', { duration: 3000 });
      return;
    }
    this.issuesOpen.set(true);
    this.reviewing.set(true);
    this.reviewError.set('');
    const reviewing = this.version();
    this.svc.review(this.htmlContent, this.fields().map(f => ({ fieldKey: f.fieldKey, label: f.label, type: f.type, role: f.role })))
      .subscribe({
        next: result => {
          this.review.set(result);
          this.reviewedVersion.set(reviewing);
          this.reviewing.set(false);
        },
        error: (e: { error?: { detail?: string } }) => {
          this.reviewing.set(false);
          this.reviewError.set(e?.error?.detail || this.translate.instant('htmlTemplateEditor.review.failed'));
        }
      });
  }

  /** Drops a finding's proposed wording into the text as its own paragraph, as one undoable step. */
  insertProposed(finding: TemplateFinding): void {
    if (!finding.proposedLanguage) return;
    const escaped = finding.proposedLanguage.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    this.history.record(() => { this.insertAtCursor(`\n<p>${escaped}</p>\n`); this.bump(); });
  }

  toggleField(i: number) {
    this.expandedIdx.update(v => v === i ? null : i);
  }

  addField() {
    const f: HtmlTemplateFieldInput = {
      fieldKey: `field${this.fields().length + 1}`,
      label: '',
      type: 'Text',
      role: 'Initiator',
      isRequired: false,
      order: this.fields().length + 1,
      filledByStep: 1
    };
    this.history.record(() => {
      this.fields.update(arr => [...arr, f]);
      this.expandedIdx.set(this.fields().length - 1);
      this.bump();
    });
  }

  removeField(i: number, e: Event) {
    e.stopPropagation();
    this.history.record(() => {
      this.fields.update(arr => arr.filter((_, idx) => idx !== i));
      if (this.expandedIdx() === i) this.expandedIdx.set(null);
      this.bump();
    });
  }

  /** True when the text has a `{{key}}` for this field — for a signature, that is where its signature will be drawn. */
  isPlaced(key: string): boolean {
    return this.htmlContent.includes(`{{${key}}}`);
  }

  /** The choices of a dropdown, typed as a comma-separated list and stored as JSON (the same shape the PDF designer uses). */
  optionsText(f: HtmlTemplateFieldInput): string { return this.optionsList(f).join(', '); }

  optionsList(f: HtmlTemplateFieldInput): string[] {
    if (!f.optionsJson) return [];
    try { return JSON.parse(f.optionsJson) as string[]; } catch { return []; }
  }

  setOptions(f: HtmlTemplateFieldInput, text: string): void {
    const list = text.split(',').map(o => o.trim()).filter(Boolean);
    f.optionsJson = list.length ? JSON.stringify(list) : '';
    // A default that is no longer one of the choices would be refused when a request is created.
    if (f.defaultValue && !list.includes(f.defaultValue)) f.defaultValue = '';
  }

  insertPlaceholder(key: string, e: Event) {
    e.stopPropagation();
    this.history.record(() => { this.insertAtCursor(`{{${key}}}`); this.bump(); });
  }

  /**
   * Opens the clause library and drops the chosen approved wording in at the cursor. Any
   * {{placeholders}} the clause carries are picked up as fields by the same scan used for pasted HTML.
   */
  openClausePicker() {
    this.dialog.open(ClausePickerDialogComponent, { width: '780px', maxWidth: '95vw' })
      .afterClosed().subscribe(clause => {
        if (!clause) return;
        this.history.record(() => {
          this.insertAtCursor(`\n${clause.body}\n`);
          this.scanForNewFields();
          this.bump();
        });
        this.snack.open(
          this.translate.instant('clauses.picker.inserted', { title: clause.title }),
          'OK', { duration: 3000 });
      });
  }

  /**
   * Wraps the selected wording so it only appears when a condition holds. Works on any selection,
   * not just an inserted clause — making existing template text conditional is the same operation.
   *
   * The markers go into the HTML content itself, so they travel into the document's immutable
   * snapshot at request time and are evaluated by the backend against the values the initiator
   * filled in. The field scanner ignores them (its regex only matches {{word}}).
   */
  wrapSelectionInCondition() {
    const ta = this.htmlEditorRef?.nativeElement;
    if (!ta || ta.selectionStart === ta.selectionEnd) {
      this.snack.open(
        this.translate.instant('htmlTemplateEditor.conditions.selectFirst'), 'OK', { duration: 4000 });
      return;
    }

    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const selected = this.htmlContent.slice(start, end);

    this.dialog.open(ConditionDialogComponent, {
      width: '620px',
      maxWidth: '95vw',
      data: {
        fields: this.fields()
          .filter(f => f.type !== 'Signature')
          .map(f => ({ fieldKey: f.fieldKey, label: f.label, type: f.type }))
      }
    }).afterClosed().subscribe((expression: string | undefined) => {
      if (!expression) return;
      this.history.record(() => {
        this.htmlContent =
          this.htmlContent.slice(0, start) +
          `${expression}\n${selected}\n{{/if}}` +
          this.htmlContent.slice(end);
        this.bump();
      });
      this.snack.open(
        this.translate.instant('htmlTemplateEditor.conditions.applied'), 'OK', { duration: 3000 });
    });
  }

  private insertAtCursor(text: string) {
    if (this.htmlEditorRef) {
      const ta = this.htmlEditorRef.nativeElement;
      const start = ta.selectionStart;
      const end = ta.selectionEnd;
      this.htmlContent = this.htmlContent.slice(0, start) + text + this.htmlContent.slice(end);
      setTimeout(() => { ta.selectionStart = ta.selectionEnd = start + text.length; ta.focus(); }, 0);
    } else {
      this.htmlContent += text;
    }
  }

  sanitizeKey(f: HtmlTemplateFieldInput) {
    f.fieldKey = f.fieldKey.replace(/[^a-zA-Z0-9_]/g, '').replace(/^[0-9]/, '_');
  }

  /** After pasting raw HTML, scan for {{placeholder}} keys not yet in the field list and add them. */
  onHtmlPaste() {
    setTimeout(() => { this.scanForNewFields(); this.bump(); }, 0);
  }

  private scanForNewFields() {
    const existingKeys = new Set(this.fields().map(f => f.fieldKey));
    const foundKeys: string[] = [];
    const seen = new Set<string>();
    const regex = /\{\{(\w+)\}\}/g;
    let m: RegExpExecArray | null;
    while ((m = regex.exec(this.htmlContent)) !== null) {
      if (!seen.has(m[1])) { seen.add(m[1]); foundKeys.push(m[1]); }
    }

    const newKeys = foundKeys.filter(k => !existingKeys.has(k));
    if (newKeys.length === 0) return;

    const startOrder = this.fields().length;
    const newFields: HtmlTemplateFieldInput[] = newKeys.map((key, i) => ({
      fieldKey: key,
      label: this.humanizeKey(key),
      type: 'Text',
      role: 'Initiator',
      isRequired: false,
      order: startOrder + i + 1,
      filledByStep: 1
    }));
    this.fields.update(arr => [...arr, ...newFields]);

    const message = newKeys.length === 1
      ? this.translate.instant('htmlTemplateEditor.fieldsScanned', { count: newKeys.length })
      : this.translate.instant('htmlTemplateEditor.fieldsScannedPlural', { count: newKeys.length });
    this.snack.open(message, 'OK', { duration: 3000 });
  }

  private humanizeKey(key: string): string {
    const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }

  toPlaceholder(key: string) { return `{{${key}}}`; }

  closeAiPanel(e: Event) { this.aiPanelOpen.set(false); }

  generateWithAi() {
    if (!this.aiPrompt.trim()) return;
    this.aiGenerating.set(true);
    this.svc.generateFromText(this.aiPrompt).subscribe({
      next: result => {
        this.history.record(() => {
        this.htmlContent = result.htmlContent;
        if (result.suggestedName && !this.name) this.name = result.suggestedName;
        this.fields.set(result.fields.map((f, i) => ({
          fieldKey: f.fieldKey,
          label: f.label,
          type: f.type as HtmlFieldType,
          role: f.role as HtmlFieldRole,
          isRequired: f.isRequired,
          order: f.order || i + 1,
          autoFillFrom: f.autoFillFrom ?? '',
          filledByStep: 1
        })));
        this.expandedIdx.set(null);
        this.bump();
        });
        this.aiGenerating.set(false);
        this.aiPanelOpen.set(false);
        this.snack.open(this.translate.instant('htmlTemplateEditor.templateGenerated'), 'OK', { duration: 4000 });
      },
      error: () => {
        this.aiGenerating.set(false);
        this.snack.open(this.translate.instant('htmlTemplateEditor.aiGenerationFailed'), 'OK', { duration: 3000 });
      }
    });
  }

  iconFor(type: HtmlFieldType): string {
    return FIELD_TYPES.find(t => t.value === type)?.icon ?? 'text_fields';
  }

  save() {
    if (!this.name.trim()) {
      this.snack.open(this.translate.instant('htmlTemplateEditor.nameRequired'), 'OK', { duration: 3000 });
      return;
    }
    if (!this.htmlContent.trim()) {
      this.snack.open(this.translate.instant('htmlTemplateEditor.htmlContentRequired'), 'OK', { duration: 3000 });
      return;
    }
    this.saving.set(true);

    const fieldsPayload = this.fields().map((f, i) => ({
      ...f,
      order: i + 1,
      autoFillFrom: f.autoFillFrom || undefined,
      optionsJson: f.optionsJson || undefined,
      defaultValue: f.defaultValue || undefined,
      filledByStep: f.filledByStep || 1
    }));

    if (this.isEdit && this.templateId) {
      this.svc.update(this.templateId, {
        name: this.name,
        htmlContent: this.htmlContent,
        description: this.description || undefined,
        isActive: this.isActive,
        fields: fieldsPayload,
        workflowType: this.workflowType(),
        hideRecipientIdentities: this.hideRecipientIdentities(),
        requestTypeId: this.requestTypeId || undefined,
        workflowPresetId: this.workflowPresetId() || undefined
      }).subscribe({
        next: () => {
          this.saving.set(false);
          this.snack.open(this.translate.instant('htmlTemplateEditor.templateUpdated'), 'OK', { duration: 3000 });
          this.router.navigate(['/admin/html-templates']);
        },
        error: () => { this.saving.set(false); this.snack.open(this.translate.instant('htmlTemplateEditor.failedToSave'), 'OK', { duration: 3000 }); }
      });
    } else {
      this.svc.create({
        name: this.name,
        htmlContent: this.htmlContent,
        description: this.description || undefined,
        fields: fieldsPayload,
        workflowType: this.workflowType(),
        hideRecipientIdentities: this.hideRecipientIdentities(),
        requestTypeId: this.requestTypeId || undefined,
        workflowPresetId: this.workflowPresetId() || undefined
      }).subscribe({
        next: () => {
          this.saving.set(false);
          this.snack.open(this.translate.instant('htmlTemplateEditor.templateCreated'), 'OK', { duration: 3000 });
          this.router.navigate(['/admin/html-templates']);
        },
        error: () => { this.saving.set(false); this.snack.open(this.translate.instant('htmlTemplateEditor.failedToCreate'), 'OK', { duration: 3000 }); }
      });
    }
  }
}
