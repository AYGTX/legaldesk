import { Component, OnInit, inject, signal, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ClausePickerDialogComponent } from '../clauses/clause-picker-dialog.component';
import { ConditionDialogComponent } from './condition-dialog.component';
import { HtmlTemplateService } from '../../core/services/html-template.service';
import { UserService } from '../../core/services/user.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { HtmlTemplateFieldInput, HtmlFieldType, HtmlFieldRole } from '../../core/models/html-template.models';
import { WorkflowType } from '../../core/models/document.models';
import { UserDto, RequestType } from '../../core/models';
import { formatSla } from '../../core/utils/sla';

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

@Component({
  selector: 'sf-html-template-editor',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule,
    MatSnackBarModule, MatTooltipModule, MatDialogModule, TranslatePipe
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

      <!-- Meta row -->
      <div class="meta-row">
        <div class="meta-field">
          <label>{{ 'common.description' | translate }}</label>
          <input [(ngModel)]="description" [placeholder]="'htmlTemplateEditor.descriptionPlaceholder' | translate" class="meta-input" />
        </div>
        <div class="meta-field">
          <label>{{ 'htmlTemplateEditor.defaultApprover' | translate }}</label>
          <select [(ngModel)]="defaultApproverId" class="meta-select">
            <option value="">{{ 'htmlTemplateEditor.userPicksAtSubmit' | translate }}</option>
            @for (u of users(); track u.id) {
              <option [value]="u.id">{{ u.fullName }} ({{ u.email }})</option>
            }
          </select>
        </div>
        <div class="meta-field">
          <label>{{ 'htmlTemplateEditor.requestType' | translate }}</label>
          <select [(ngModel)]="requestTypeId" class="meta-select">
            <option value="">{{ 'htmlTemplateEditor.noRequestType' | translate }}</option>
            @for (rt of requestTypes(); track rt.id) {
              <option [value]="rt.id">{{ rt.name }} ({{ formatSla(rt.slaHours) }})</option>
            }
          </select>
        </div>
        <div class="meta-field">
          <label>{{ 'htmlTemplateEditor.workflowType' | translate }}</label>
          <div class="wf-toggle-group">
            @for (w of workflowTypes; track w.value) {
              <button type="button" class="wf-toggle-btn" [class.active]="workflowType() === w.value" (click)="workflowType.set(w.value)">
                {{ w.labelKey | translate }}
              </button>
            }
          </div>
        </div>
        <div class="meta-field meta-toggle">
          <label>{{ 'designer.anon.hideIdentities' | translate }}</label>
          <button class="toggle-btn" [class.on]="hideRecipientIdentities()" (click)="hideRecipientIdentities.set(!hideRecipientIdentities())" [matTooltip]="'designer.anon.hideIdentitiesHint' | translate">
            {{ hideRecipientIdentities() ? ('common.yes' | translate) : ('common.no' | translate) }}
          </button>
        </div>
        @if (isEdit) {
          <div class="meta-field meta-toggle">
            <label>{{ 'htmlTemplateEditor.active' | translate }}</label>
            <button class="toggle-btn" [class.on]="isActive" (click)="isActive = !isActive">
              {{ isActive ? ('htmlTemplateEditor.active' | translate) : ('htmlTemplateEditor.inactive' | translate) }}
            </button>
          </div>
        }
      </div>

      <!-- Editor body -->
      <div class="editor-body">
        <!-- HTML editor / preview -->
        <div class="html-panel">
          <div class="panel-header">
            <span class="panel-label">{{ 'htmlTemplateEditor.htmlContentLabel' | translate }}</span>
            <button class="clause-lib-btn" (click)="openClausePicker()"
                    [matTooltip]="'clauses.picker.buttonTooltip' | translate">
              <mat-icon>menu_book</mat-icon> {{ 'clauses.picker.button' | translate }}
            </button>
            <button class="clause-lib-btn" (click)="wrapSelectionInCondition()"
                    [matTooltip]="'htmlTemplateEditor.conditions.buttonTooltip' | translate">
              <mat-icon>rule</mat-icon> {{ 'htmlTemplateEditor.conditions.button' | translate }}
            </button>
            <span class="panel-hint" [innerHTML]="'htmlTemplateEditor.panelHint' | translate: { example: curlyExample }"></span>
          </div>
          @if (previewing()) {
            <div class="preview-pane" [innerHTML]="previewHtml"></div>
          } @else {
            <textarea #htmlEditor class="html-textarea" [(ngModel)]="htmlContent"
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
                  <div class="field-card-body">
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
                      <div class="fc-row">
                        <label>{{ 'htmlTemplateEditor.assignTo' | translate }} <span class="fc-hint">{{ 'htmlTemplateEditor.assignToHint' | translate }}</span></label>
                        <select [(ngModel)]="f.preAssignedApproverId" class="fc-select">
                          <option value="">{{ 'htmlTemplateEditor.unassigned' | translate }}</option>
                          @for (u of users(); track u.id) {
                            <option [value]="u.id">{{ u.fullName }} ({{ u.email }})</option>
                          }
                        </select>
                      </div>
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
                          <input type="number" min="1" [(ngModel)]="f.workflowStage" class="fc-input" />
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
                <span class="material-symbols-outlined">smart_toy</span> Groq AI
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

    /* Meta row */
    .meta-row { display: flex; gap: 16px; padding: 12px 0; border-bottom: 1px solid #e8edf2; align-items: flex-start; flex-wrap: wrap; }
    .meta-field { display: flex; flex-direction: column; gap: 4px; min-width: 200px; }
    .meta-field label { font-size: 11px; font-weight: 500; color: #5f6368; text-transform: uppercase; letter-spacing: 0.5px; }
    .meta-input, .meta-select { height: 36px; padding: 0 12px; border-radius: 8px; border: 1px solid #c7d0dc; background: #fff; font-size: 13px; font-family: inherit; outline: none; }
    .meta-input:focus, .meta-select:focus { border-color: #0b57d0; }
    .meta-select { min-width: 240px; }
    .meta-toggle { flex-direction: row; align-items: center; gap: 12px; }
    .toggle-btn { height: 32px; padding: 0 16px; border-radius: 16px; border: 1px solid #c7d0dc; background: #fff; font-size: 13px; font-family: inherit; cursor: pointer; }
    .toggle-btn.on { background: #d3e3fd; border-color: #0b57d0; color: #0b57d0; font-weight: 500; }

    .wf-toggle-group { display: flex; border: 1px solid #c7d0dc; border-radius: 8px; overflow: hidden; height: 36px; }
    .wf-toggle-btn { flex: 1; border: none; background: #fff; font-size: 12px; font-family: inherit; cursor: pointer; padding: 0 10px; border-right: 1px solid #c7d0dc; color: #5f6368; }
    .wf-toggle-btn:last-child { border-right: none; }
    .wf-toggle-btn.active { background: #0b57d0; color: #fff; font-weight: 500; }
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
    .preview-pane { flex: 1; overflow: auto; padding: 24px; font-family: 'Google Sans Text', sans-serif; font-size: 14px; line-height: 1.6; }

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
  readonly formatSla = formatSla;

  name = '';
  description = '';
  htmlContent = '';
  defaultApproverId = '';
  requestTypeId = '';
  isActive = true;

  aiPanelOpen = signal(false);
  aiGenerating = signal(false);
  aiPrompt = '';

  private readonly sanitizer = inject(DomSanitizer);
  get previewHtml(): SafeHtml { return this.sanitizer.bypassSecurityTrustHtml(this.htmlContent); }

  ngOnInit() {
    this.templateId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.templateId;

    this.userSvc.getUsers({ pageSize: 200 }).subscribe(r => this.users.set(r.items));
    this.requestTypeSvc.getActive().subscribe(types => this.requestTypes.set(types));

    if (this.isEdit && this.templateId) {
      this.svc.getById(this.templateId).subscribe(t => {
        this.name = t.name;
        this.description = t.description ?? '';
        this.htmlContent = t.htmlContent;
        this.defaultApproverId = t.defaultApproverId ?? '';
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
          preAssignedApproverId: f.preAssignedApproverId ?? '',
          workflowStage: f.workflowStage ?? 1
        })));
      });
    }
  }

  togglePreview() { this.previewing.update(v => !v); }

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
      workflowStage: 1
    };
    this.fields.update(arr => [...arr, f]);
    this.expandedIdx.set(this.fields().length - 1);
  }

  removeField(i: number, e: Event) {
    e.stopPropagation();
    this.fields.update(arr => arr.filter((_, idx) => idx !== i));
    if (this.expandedIdx() === i) this.expandedIdx.set(null);
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
    this.insertAtCursor(`{{${key}}}`);
  }

  /**
   * Opens the clause library and drops the chosen approved wording in at the cursor. Any
   * {{placeholders}} the clause carries are picked up as fields by the same scan used for pasted HTML.
   */
  openClausePicker() {
    this.dialog.open(ClausePickerDialogComponent, { width: '780px', maxWidth: '95vw' })
      .afterClosed().subscribe(clause => {
        if (!clause) return;
        this.insertAtCursor(`\n${clause.body}\n`);
        this.scanForNewFields();
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
      this.htmlContent =
        this.htmlContent.slice(0, start) +
        `${expression}\n${selected}\n{{/if}}` +
        this.htmlContent.slice(end);
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
    setTimeout(() => this.scanForNewFields(), 0);
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
      workflowStage: 1
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
          workflowStage: 1
        })));
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
      preAssignedApproverId: f.preAssignedApproverId || undefined,
      workflowStage: f.workflowStage || 1
    }));

    if (this.isEdit && this.templateId) {
      this.svc.update(this.templateId, {
        name: this.name,
        htmlContent: this.htmlContent,
        description: this.description || undefined,
        defaultApproverId: this.defaultApproverId || undefined,
        isActive: this.isActive,
        fields: fieldsPayload,
        workflowType: this.workflowType(),
        hideRecipientIdentities: this.hideRecipientIdentities(),
        requestTypeId: this.requestTypeId || undefined
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
        defaultApproverId: this.defaultApproverId || undefined,
        fields: fieldsPayload,
        workflowType: this.workflowType(),
        hideRecipientIdentities: this.hideRecipientIdentities(),
        requestTypeId: this.requestTypeId || undefined
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
