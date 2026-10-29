import {
  Component, OnInit, OnDestroy, inject, signal, computed,
  ElementRef, ViewChild, AfterViewInit
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CdkDragDrop, DragDropModule } from '@angular/cdk/drag-drop';
import { StageLanesComponent } from '../../shared/stage-lanes/stage-lanes.component';
import { WorkflowTypeToggleComponent } from '../../shared/workflow-type-toggle/workflow-type-toggle.component';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDividerModule } from '@angular/material/divider';
import { MatSnackBarModule, MatSnackBar } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { firstValueFrom, switchMap } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { VacationService } from '../../core/services/vacation.service';
import { UserService } from '../../core/services/user.service';
import { ApprovalType, DocumentDetail, FieldDto, RecipientInputDto, FieldType, UserDto, WorkflowType } from '../../core/models';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { ScanOverlayComponent } from '../../shared/scan-overlay/scan-overlay.component';
import { v4 as uuidv4 } from 'uuid';

interface DesignerField {
  tempId: string;
  id?: string;
  fieldType: FieldType;
  pageNumber: number;
  xPosition: number;
  yPosition: number;
  width: number;
  height: number;
  isRequired: boolean;
  recipientId?: string;
  zIndex: number;
  options?: string;
  groupName?: string;
  color: string;
  label: string;
}

interface RecipientColor {
  recipientId: string;
  email: string;
  name: string;
  color: string;
  order: number;
}

const FIELD_COLORS = ['#6366f1', '#ec4899', '#f59e0b', '#22c55e', '#3b82f6', '#8b5cf6'];

const FIELD_DEFINITIONS: { type: FieldType; icon: string; label: string; width: number; height: number }[] = [
  { type: 'Signature', icon: 'draw', label: 'Signature', width: 200, height: 60 },
  { type: 'Initials', icon: 'text_fields', label: 'Initials', width: 80, height: 40 },
  { type: 'Date', icon: 'calendar_today', label: 'Date', width: 140, height: 40 },
  { type: 'Text', icon: 'short_text', label: 'Text', width: 180, height: 40 },
  { type: 'Checkbox', icon: 'check_box', label: 'Checkbox', width: 32, height: 32 },
  { type: 'Name', icon: 'person', label: 'Name', width: 180, height: 40 },
  { type: 'Email', icon: 'email', label: 'Email', width: 200, height: 40 },
  { type: 'Title', icon: 'badge', label: 'Title', width: 160, height: 40 },
  { type: 'Company', icon: 'business', label: 'Company', width: 180, height: 40 },
  { type: 'Dropdown', icon: 'arrow_drop_down_circle', label: 'Dropdown', width: 180, height: 40 },
  { type: 'Radio', icon: 'radio_button_checked', label: 'Radio', width: 140, height: 32 },
  { type: 'Stamp', icon: 'verified', label: 'Stamp', width: 100, height: 100 },
  { type: 'Attachment', icon: 'attach_file', label: 'Attachment', width: 160, height: 40 }
];

@Component({
  selector: 'sf-designer',
  standalone: true,
  imports: [
    CommonModule, RouterLink, ReactiveFormsModule, DragDropModule,
    MatToolbarModule, MatButtonModule, MatIconModule, MatSidenavModule,
    MatListModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatCheckboxModule, MatTooltipModule, MatProgressSpinnerModule,
    MatDividerModule, MatSnackBarModule, MatChipsModule, StageLanesComponent, WorkflowTypeToggleComponent, TranslatePipe
  , ScanOverlayComponent],
  template: `
    <div class="designer-wrapper">
      <!-- Top Toolbar -->
      <mat-toolbar class="designer-toolbar">
        <button mat-icon-button (click)="sidebarCollapsed.update(v => !v)" [matTooltip]="'designer.toolbar.toggleSidebar' | translate">
          <mat-icon>{{ sidebarCollapsed() ? 'menu_open' : 'menu' }}</mat-icon>
        </button>
        <button mat-icon-button [routerLink]="['/contracts', documentId]" [matTooltip]="'common.back' | translate">
          <mat-icon>arrow_back</mat-icon>
        </button>
        <span class="toolbar-title">{{ doc()?.title ?? ('common.loading' | translate) }}</span>
        <span class="flex-1"></span>

        @if (doc()) {
          <div class="page-controls">
            <button mat-icon-button (click)="prevPage()" [disabled]="currentPage() <= 1" [matTooltip]="'designer.toolbar.previousPage' | translate">
              <mat-icon>chevron_left</mat-icon>
            </button>
            <span class="page-indicator">{{ 'designer.toolbar.pageIndicator' | translate: { n: currentPage(), total: doc()!.pageCount } }}</span>
            <button mat-icon-button (click)="nextPage()" [disabled]="currentPage() >= doc()!.pageCount" [matTooltip]="'designer.toolbar.nextPage' | translate">
              <mat-icon>chevron_right</mat-icon>
            </button>
          </div>

          <div class="zoom-controls">
            <button mat-icon-button (click)="zoom(-0.25)" [matTooltip]="'designer.toolbar.zoomOut' | translate"><mat-icon>zoom_out</mat-icon></button>
            <span class="zoom-label">{{ (zoomLevel() * 100).toFixed(0) }}%</span>
            <button mat-icon-button (click)="zoom(0.25)" [matTooltip]="'designer.toolbar.zoomIn' | translate"><mat-icon>zoom_in</mat-icon></button>
          </div>

          <mat-divider vertical class="toolbar-divider"></mat-divider>

          <button class="grid-toggle" [class.active]="snapToGrid()" (click)="snapToGrid.set(!snapToGrid())"
                  [matTooltip]="'designer.toolbar.snapToGrid' | translate">
            <mat-icon>grid_on</mat-icon>
          </button>

          <mat-divider vertical class="toolbar-divider"></mat-divider>

          <button mat-icon-button (click)="undo()" [disabled]="historyIndex() <= 0" [matTooltip]="'designer.toolbar.undo' | translate">
            <mat-icon>undo</mat-icon>
          </button>
          <button mat-icon-button (click)="redo()" [disabled]="historyIndex() >= history().length - 1" [matTooltip]="'designer.toolbar.redo' | translate">
            <mat-icon>redo</mat-icon>
          </button>

          <mat-divider vertical class="toolbar-divider"></mat-divider>

          <button mat-stroked-button (click)="detectFields()" [disabled]="detecting()"
                  [matTooltip]="'pdfTemplates.designer.detectFieldsHint' | translate">
            <mat-icon>auto_awesome</mat-icon>
            {{ detecting() ? ('pdfTemplates.designer.detecting' | translate) : ('pdfTemplates.designer.detectFields' | translate) }}
          </button>

          <mat-divider vertical class="toolbar-divider"></mat-divider>

          <button mat-stroked-button (click)="save()" [disabled]="saving() || !canSave()"
                  [matTooltip]="saveButtonTooltip()">
            <mat-icon>save</mat-icon>
            {{ saving() ? ('designer.toolbar.saving' | translate) : ('common.save' | translate) }}
          </button>
          <button mat-flat-button color="primary" [routerLink]="['/contracts', documentId]">{{ 'designer.toolbar.done' | translate }}</button>
        }
      </mat-toolbar>

      <div class="designer-body">

        <!-- Left Sidebar: Recipients -->
        <div class="left-sidebar" [class.collapsed]="sidebarCollapsed()">
          @if (!sidebarCollapsed()) {
            <div class="sidebar-header">
              <span class="sidebar-title">{{ 'designer.sidebar.recipientsTitle' | translate }}</span>
              <button mat-icon-button class="sidebar-collapse-btn" (click)="sidebarCollapsed.set(true)" [matTooltip]="'designer.sidebar.collapse' | translate">
                <mat-icon>chevron_left</mat-icon>
              </button>
            </div>

            <!-- How the people act: same toggle and wording as the request wizard and the template designer -->
            <div class="wf-wrap">
              <sf-workflow-type-toggle [compact]="true" [value]="workflowType()" (valueChange)="setWorkflowType($event)"></sf-workflow-type-toggle>
            </div>

            <!-- Anonymous recipients toggle -->
            <div class="anon-toggle" (click)="hideRecipientIdentities.set(!hideRecipientIdentities())"
                 [class.anon-on]="hideRecipientIdentities()">
              <mat-icon class="anon-icon">{{ hideRecipientIdentities() ? 'visibility_off' : 'visibility' }}</mat-icon>
              <div class="anon-text">
                <span class="anon-label">{{ 'designer.anon.hideIdentities' | translate }}</span>
                <span class="anon-sub">{{ 'designer.anon.hideIdentitiesHint' | translate }}</span>
              </div>
              <div class="anon-switch" [class.on]="hideRecipientIdentities()">
                <div class="anon-thumb"></div>
              </div>
            </div>

            <!-- Add Recipient sits at the top -->
            @if (!showAddRecipient()) {
              <button class="add-recipient-btn" (click)="openAddRecipient()">
                <mat-icon>person_add</mat-icon>
                <span>{{ 'designer.recipients.addRecipient' | translate }}</span>
              </button>
            } @else {
              <!-- Recipient picker: user list + "Other" -->
              <div class="add-recipient-panel">
                <div class="add-rec-tabs">
                  <button class="add-rec-tab" [class.active]="recipientMode() === 'pick'"
                          (click)="recipientMode.set('pick')">{{ 'designer.recipients.fromUsers' | translate }}</button>
                  <button class="add-rec-tab" [class.active]="recipientMode() === 'manual'"
                          (click)="recipientMode.set('manual')">{{ 'designer.recipients.other' | translate }}</button>
                </div>

                @if (recipientMode() === 'pick') {
                  <div class="user-picker">
                    <input class="user-search" type="text" [placeholder]="'designer.recipients.searchUsersPlaceholder' | translate"
                           [value]="userSearch()"
                           (input)="userSearch.set($any($event.target).value)" />
                    @if (usersLoading()) {
                      <div class="user-loading"><mat-spinner diameter="20"></mat-spinner></div>
                    } @else {
                      <div class="user-list">
                        @for (u of filteredUsers(); track u.id) {
                          <div class="user-item" (click)="addRecipientFromUser(u)">
                            <div class="user-avatar">{{ u.fullName.charAt(0).toUpperCase() }}</div>
                            <div class="user-info">
                              <span class="user-name">{{ u.fullName }}</span>
                              <span class="user-email">{{ u.email }}</span>
                            </div>
                            <mat-icon class="user-add-icon">add_circle</mat-icon>
                          </div>
                        }
                        @if (filteredUsers().length === 0) {
                          <p class="no-users">{{ 'designer.recipients.noUsersFound' | translate }}</p>
                        }
                      </div>
                    }
                  </div>
                } @else {
                  <form [formGroup]="recipientForm" (ngSubmit)="addRecipient()" class="manual-recipient-form">
                    <input class="manual-input" type="text" [placeholder]="'designer.recipients.fullNamePlaceholder' | translate" formControlName="name" />
                    <input class="manual-input" type="email" [placeholder]="'designer.recipients.emailAddressPlaceholder' | translate" formControlName="email" />
                    <div class="manual-form-actions">
                      <button type="button" class="btn-cancel" (click)="showAddRecipient.set(false)">{{ 'common.cancel' | translate }}</button>
                      <button type="submit" class="btn-add" [disabled]="recipientForm.invalid">{{ 'designer.recipients.addBtn' | translate }}</button>
                    </div>
                  </form>
                }

                <button class="btn-cancel-add" (click)="showAddRecipient.set(false)">
                  <mat-icon>close</mat-icon> {{ 'common.close' | translate }}
                </button>
              </div>
            }

            @if (recipients().length === 0) {
              <p class="sidebar-hint">{{ 'designer.recipients.addAtLeastOneHint' | translate }}</p>
            }

            <ng-template #chip let-rc let-i="index">
                @let fieldCount = recipientFieldCounts().get(rc.recipientId) ?? 0;
                <div class="recipient-chip"
                     [class.selected-recipient]="selectedRecipientId() === rc.recipientId"
                     [class.no-fields-chip]="fieldCount === 0 && recipients().length > 0 && isSignerRole(rc.recipientId)"
                     (click)="selectRecipient(rc.recipientId)"
                     [style.border-left-color]="rc.color">

                  <!-- Left: order badge + info -->
                  <div class="rc-left">
                    <div class="order-badge" [style.background]="rc.color"
                         [class.parallel-badge]="workflowType() === 'Mixed' && parallelOrders().has(rc.order)">
                      @if (workflowType() === 'Parallel') { ∥ } @else { {{ rc.order }} }
                    </div>
                    <div class="recipient-chip-info">
                      <span class="rc-name">{{ rc.name }}</span>
                      <span class="rc-email">{{ rc.email }}</span>
                    </div>
                  </div>

                  <!-- Right: field count + controls -->
                  <div class="rc-right" (click)="$event.stopPropagation()">
                    @if (isSignerRole(rc.recipientId)) {
                      <span class="field-count" [class.count-zero]="fieldCount === 0">
                        @if (fieldCount === 0) {
                          <mat-icon class="warn-icon">warning</mat-icon>
                        }
                        {{ fieldCount }}
                      </span>
                    }
                    <div class="rc-actions">
                      @if (workflowType() === 'Sequential') {
                        <button class="rc-btn" [disabled]="i === 0" (click)="moveRecipient(i, -1)" [title]="'designer.recipients.moveUp' | translate">▲</button>
                        <button class="rc-btn" [disabled]="i === recipients().length - 1" (click)="moveRecipient(i, 1)" [title]="'designer.recipients.moveDown' | translate">▼</button>
                      } @else if (workflowType() === 'Mixed') {
                        <button class="rc-btn" [disabled]="rc.order <= 1" (click)="changeStage(i, -1)" [title]="'designer.recipients.earlierStage' | translate">▲</button>
                        <button class="rc-btn" (click)="changeStage(i, 1)" [title]="'designer.recipients.laterStage' | translate">▼</button>
                      }
                      <button class="rc-btn rc-btn-delete" (click)="removeRecipient(i)" [title]="'designer.recipients.remove' | translate">✕</button>
                    </div>
                  </div>

                  <!-- Participant role + free-text label -->
                  <div class="rc-role-row" (click)="$event.stopPropagation()">
                    <select class="rc-role-select" [value]="recipientRole(rc.recipientId)"
                            (change)="setRecipientRole(rc.recipientId, $any($event.target).value)">
                      @for (role of participantRoles; track role) {
                        <option [value]="role">{{ ('designer.roles.' + role) | translate }}</option>
                      }
                    </select>
                    @if (selectedRecipientId() === rc.recipientId) {
                    <input class="rc-role-label" type="text" maxlength="100"
                           [placeholder]="'designer.recipients.roleLabelPlaceholder' | translate"
                           [value]="recipientRoleLabelOf(rc.recipientId)"
                           (change)="setRecipientRoleLabel(rc.recipientId, $any($event.target).value)" />
                    }
                  </div>
                </div>
            </ng-template>

            @if (workflowType() === 'Mixed') {
              <div class="recipient-lanes">
                <sf-stage-lanes [items]="stageItems()" (changed)="onStagesChanged($any($event))">
                  <ng-template #card let-item>
                    <ng-container *ngTemplateOutlet="chip; context: { $implicit: item.rc, index: item.index }"></ng-container>
                  </ng-template>
                </sf-stage-lanes>
              </div>
            } @else {
              <div class="recipient-list">
                @for (rc of recipientColors(); track rc.recipientId; let i = $index) {
                  <ng-container *ngTemplateOutlet="chip; context: { $implicit: rc, index: i }"></ng-container>
                }
              </div>
            }
          } @else {
            <!-- Collapsed state: icon strip -->
            <button mat-icon-button class="sidebar-expand-btn" (click)="sidebarCollapsed.set(false)" [matTooltip]="'designer.sidebar.expand' | translate">
              <mat-icon>menu_open</mat-icon>
            </button>
            <div class="collapsed-recipients">
              @for (rc of recipientColors(); track rc.recipientId; let i = $index) {
                <div class="collapsed-dot"
                     [class.selected-recipient-dot]="selectedRecipientId() === rc.recipientId"
                     [style.background]="rc.color"
                     (click)="selectRecipient(rc.recipientId)"
                     [matTooltip]="rc.name + ' (' + rc.email + ')'">
                  {{ i + 1 }}
                </div>
              }
            </div>
          }
        </div>

        <!-- Main area: field toolbar (top) + canvas (bottom) -->
        <div class="main-area">

          <!-- Field palette toolbar -->
          <div class="field-toolbar"
               cdkDropList #paletteList="cdkDropList"
               [cdkDropListData]="fieldDefs"
               [cdkDropListConnectedTo]="canvasList ? [canvasList] : []"
               cdkDropListSortingDisabled>

            @if (recipients().length > 0) {
              <!-- Active recipient indicator -->
              @if (selectedRecipientId(); as selId) {
                @if (activeRecipient(); as ar) {
                  <div class="active-recipient-badge" [style.background]="ar.color">
                    <mat-icon>person</mat-icon> {{ 'designer.palette.droppingFor' | translate: { name: ar.name } }}
                  </div>
                }
              } @else {
                <div class="active-recipient-badge warn">
                  <mat-icon>warning</mat-icon> {{ 'designer.palette.selectRecipientWarning' | translate }}
                </div>
              }
            }

            @for (fd of fieldDefs; track fd.type) {
              <div class="field-chip" [cdkDragData]="fd" cdkDrag
                   [matTooltip]="('designer.fieldTypes.' + fd.type) | translate" matTooltipPosition="below">
                <div *cdkDragPreview class="drag-preview">
                  <mat-icon>{{ fd.icon }}</mat-icon>
                  <span>{{ ('designer.fieldTypes.' + fd.type) | translate }}</span>
                </div>
                <mat-icon>{{ fd.icon }}</mat-icon>
                <span>{{ ('designer.fieldTypes.' + fd.type) | translate }}</span>
              </div>
            }
          </div>

          <!-- Canvas area -->
          <div class="canvas-area" #canvasContainer>
            @if (pdfLoading()) {
              <div class="pdf-loading"><mat-spinner diameter="48"></mat-spinner></div>
            }

            <div class="pdf-viewport" [style.transform]="'scale(' + zoomLevel() + ')'" [style.transform-origin]="'top center'">
              <div class="pdf-canvas-wrapper" #pdfWrapper
                   cdkDropList #canvasList="cdkDropList"
                   [cdkDropListData]="pageFields()"
                   cdkDropListSortingDisabled
                   (cdkDropListDropped)="onDrop($event)"
                   [style.width.px]="canvasWidth" [style.height.px]="canvasHeight"
                   (click)="onCanvasClick($event)">

                <canvas #pdfCanvas class="pdf-canvas"></canvas>
                @if (detecting()) { <sf-scan-overlay /> }
                @if (snapToGrid()) {
                  <div class="grid-overlay" [style.background-size]="gridSize + 'px ' + gridSize + 'px'"></div>
                }

                @for (field of pageFields(); track field.tempId) {
                  <div class="placed-field"
                       [class.selected-field]="selectedField()?.tempId === field.tempId"
                       [style.left.px]="field.xPosition" [style.top.px]="field.yPosition"
                       [style.width.px]="field.width" [style.height.px]="field.height"
                       [style.border-color]="fieldColor(field)"
                       [style.background]="fieldBg(field)"
                       (mousedown)="startFieldDrag($event, field)"
                       (click)="selectField(field, $event)"
                       [matTooltip]="fieldTooltip(field)">
                    <div class="field-header" [style.background]="fieldColor(field)">
                      <mat-icon>{{ fieldIcon(field.fieldType) }}</mat-icon>
                      <span>{{ ('designer.fieldTypes.' + field.fieldType) | translate }}</span>
                      <div class="field-recipient-dot" [style.background]="fieldRecipientColor(field)"></div>
                    </div>
                    <div class="field-label">
                      {{ field.label || (('designer.fieldTypes.' + field.fieldType) | translate) }}
                      @if (field.fieldType === 'Radio' && field.groupName) {
                        <span class="field-group-tag">{{ field.groupName }}</span>
                      }
                    </div>
                    <div class="resize-handle" (mousedown)="startResize($event, field)"></div>
                    <button class="field-delete-btn" mat-icon-button
                            (click)="deleteField(field, $event)" [matTooltip]="'designer.canvas.removeFieldTooltip' | translate">
                      <mat-icon>close</mat-icon>
                    </button>
                  </div>
                }
              </div>
            </div>
          </div>
        </div>

        <!-- Right Panel: Field Properties -->
        @if (selectedField()) {
          <div class="right-panel">
            <div class="panel-section">
              <div class="properties-header">
                <h3 class="panel-heading">{{ 'designer.properties.heading' | translate }}</h3>
                <button mat-icon-button (click)="selectedField.set(null)"><mat-icon>close</mat-icon></button>
              </div>

              <div class="property-group">
                <label class="property-label">{{ 'designer.properties.fieldType' | translate }}</label>
                <div class="field-type-display">
                  <mat-icon>{{ fieldIcon(selectedField()!.fieldType) }}</mat-icon>
                  {{ ('designer.fieldTypes.' + selectedField()!.fieldType) | translate }}
                </div>
              </div>

              <div class="property-group">
                <label class="property-label">{{ 'designer.properties.fieldLabel' | translate }}</label>
                <mat-form-field appearance="outline" class="full-width">
                  <input matInput maxlength="100" [value]="selectedField()!.label"
                         [placeholder]="'designer.properties.fieldLabelPlaceholder' | translate"
                         (change)="updateFieldProp('label', $any($event.target).value.trim())" />
                </mat-form-field>
              </div>

              <div class="property-group">
                <label class="property-label">{{ 'designer.properties.assignedTo' | translate }}</label>
                <div class="recipient-select">
                  @for (rc of recipientColors(); track rc.recipientId) {
                    <div class="recipient-option"
                         [class.selected]="selectedField()!.recipientId === rc.recipientId"
                         (click)="assignFieldToRecipient(rc.recipientId)"
                         [style.border-color]="rc.color">
                      <div class="recipient-dot" [style.background]="rc.color"></div>
                      <span>{{ rc.name }}</span>
                    </div>
                  }
                </div>
              </div>

              <div class="property-group">
                <label class="property-label">{{ 'common.required' | translate }}</label>
                <mat-checkbox [checked]="selectedField()!.isRequired"
                              (change)="updateFieldProp('isRequired', $event.checked)">
                  {{ 'designer.properties.requiredCheckbox' | translate }}
                </mat-checkbox>
              </div>

              @if (selectedField()!.fieldType === 'Radio') {
                <div class="property-group">
                  <label class="property-label">{{ 'designer.properties.radioGroup' | translate }}</label>
                  <mat-form-field appearance="outline" class="full-width">
                    <input matInput [value]="selectedField()!.groupName ?? ''"
                           [placeholder]="'designer.properties.radioGroupPlaceholder' | translate"
                           (change)="updateFieldProp('groupName', $any($event.target).value.trim() || undefined)" />
                  </mat-form-field>
                  <p class="property-hint">{{ 'designer.properties.radioGroupHint' | translate }}</p>
                </div>
              }

              <div class="property-group">
                <label class="property-label">{{ 'designer.properties.position' | translate }}</label>
                <div class="position-inputs">
                  <mat-form-field appearance="outline" class="pos-input">
                    <mat-label>X</mat-label>
                    <input matInput type="number" [value]="selectedField()!.xPosition"
                           (change)="updateFieldProp('xPosition', +$any($event.target).value)" />
                  </mat-form-field>
                  <mat-form-field appearance="outline" class="pos-input">
                    <mat-label>Y</mat-label>
                    <input matInput type="number" [value]="selectedField()!.yPosition"
                           (change)="updateFieldProp('yPosition', +$any($event.target).value)" />
                  </mat-form-field>
                </div>
              </div>

              <div class="property-group">
                <label class="property-label">{{ 'designer.properties.size' | translate }}</label>
                <div class="position-inputs">
                  <mat-form-field appearance="outline" class="pos-input">
                    <mat-label>W</mat-label>
                    <input matInput type="number" [value]="selectedField()!.width"
                           (change)="updateFieldProp('width', +$any($event.target).value)" />
                  </mat-form-field>
                  <mat-form-field appearance="outline" class="pos-input">
                    <mat-label>H</mat-label>
                    <input matInput type="number" [value]="selectedField()!.height"
                           (change)="updateFieldProp('height', +$any($event.target).value)" />
                  </mat-form-field>
                </div>
              </div>

              <button mat-stroked-button color="warn" class="full-width" (click)="deleteField(selectedField()!, $event)">
                <mat-icon>delete</mat-icon> {{ 'designer.properties.removeField' | translate }}
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styleUrl: './designer.component.scss'
})
export class DesignerComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('pdfCanvas') pdfCanvasRef!: ElementRef<HTMLCanvasElement>;
  @ViewChild('pdfWrapper') pdfWrapperRef!: ElementRef<HTMLDivElement>;

  private readonly route = inject(ActivatedRoute);
  private readonly documentService = inject(DocumentService);
  private readonly vacationService = inject(VacationService);
  private readonly pdfTemplateService = inject(PdfTemplateService);
  private readonly userService = inject(UserService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly fb = inject(FormBuilder);
  private readonly translate = inject(TranslateService);

  readonly fieldDefs = FIELD_DEFINITIONS;

  readonly doc = signal<DocumentDetail | null>(null);
  readonly pdfLoading = signal(true);
  readonly currentPage = signal(1);
  readonly zoomLevel = signal(1.0);
  readonly saving = signal(false);
  readonly selectedField = signal<DesignerField | null>(null);
  readonly selectedRecipientId = signal<string | null>(null);
  readonly showAddRecipient = signal(false);
  readonly sidebarCollapsed = signal(false);
  readonly snapToGrid = signal(true);
  readonly gridSize = 10;

  readonly workflowType = signal<WorkflowType>('Sequential');
  readonly hideRecipientIdentities = signal(false);

  readonly allFields = signal<DesignerField[]>([]);
  readonly recipients = signal<RecipientInputDto[]>([]);
  readonly detecting = signal(false);
  readonly canSave = computed(() => this.recipients().length > 0 && this.allFields().length > 0);

  readonly history = signal<DesignerField[][]>([[]]);
  readonly historyIndex = signal(0);

  // User picker
  readonly usersList = signal<UserDto[]>([]);
  readonly usersLoading = signal(false);
  readonly userSearch = signal('');
  readonly recipientMode = signal<'pick' | 'manual'>('pick');

  documentId = '';
  canvasWidth = 794;
  canvasHeight = 1123;
  private suppressCanvasClick = false;

  private pdfDoc: unknown = null;

  readonly recipientForm = this.fb.group({
    name: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    order: [1]
  });

  readonly recipientColors = computed<RecipientColor[]>(() =>
    this.recipients().map((r, i) => ({
      recipientId: r.id ?? `temp-${i}`,
      email: r.email,
      name: r.name,
      color: FIELD_COLORS[i % FIELD_COLORS.length],
      order: r.order ?? i + 1
    }))
  );

  readonly activeRecipient = computed(() =>
    this.recipientColors().find(r => r.recipientId === this.selectedRecipientId()) ?? null
  );

  readonly recipientFieldCounts = computed<Map<string, number>>(() => {
    const counts = new Map<string, number>();
    for (const f of this.allFields()) {
      if (f.recipientId) counts.set(f.recipientId, (counts.get(f.recipientId) ?? 0) + 1);
    }
    return counts;
  });

  readonly parallelOrders = computed<Set<number>>(() => {
    if (this.workflowType() !== 'Mixed') return new Set();
    const counts = new Map<number, number>();
    for (const r of this.recipients()) counts.set(r.order, (counts.get(r.order) ?? 0) + 1);
    return new Set([...counts.entries()].filter(([, c]) => c > 1).map(([o]) => o));
  });

  readonly filteredUsers = computed(() => {
    const q = this.userSearch().toLowerCase().trim();
    const existing = new Set(this.recipients().map(r => r.email.toLowerCase()));
    return this.usersList()
      .filter(u => !existing.has(u.email.toLowerCase()))
      .filter(u => !q || u.fullName.toLowerCase().includes(q) || u.email.toLowerCase().includes(q));
  });

  readonly pageFields = computed(() =>
    this.allFields().filter(f => f.pageNumber === this.currentPage())
  );

  fieldColor(field: DesignerField): string {
    return this.recipientColors().find(r => r.recipientId === field.recipientId)?.color ?? '#6366f1';
  }
  fieldBg(field: DesignerField): string { return `${this.fieldColor(field)}14`; }
  fieldIcon(type: string): string { return FIELD_DEFINITIONS.find(f => f.type === type)?.icon ?? 'edit'; }
  fieldTooltip(field: DesignerField): string {
    const rc = this.recipientColors().find(r => r.recipientId === field.recipientId);
    const typeLabel = this.translate.instant('designer.fieldTypes.' + field.fieldType);
    const requiredSuffix = field.isRequired ? ` ${this.translate.instant('designer.canvas.requiredSuffix')}` : '';
    return `${typeLabel}${rc ? ' → ' + rc.name : ''}${requiredSuffix}`;
  }
  fieldRecipientColor(field: DesignerField): string {
    return this.recipientColors().find(r => r.recipientId === field.recipientId)?.color ?? 'transparent';
  }
  saveButtonTooltip(): string {
    if (this.canSave()) return '';
    return this.recipients().length === 0
      ? this.translate.instant('designer.toolbar.saveDisabledNoRecipient')
      : this.translate.instant('designer.toolbar.saveDisabledNoField');
  }

  ngOnInit(): void {
    this.documentId = this.route.snapshot.paramMap.get('id')!;

    this.documentService.getDocument(this.documentId).subscribe({
      next: doc => {
        this.doc.set(doc);
        if (doc.workflowType) this.workflowType.set(doc.workflowType);
        this.hideRecipientIdentities.set(doc.hideRecipientIdentities ?? false);
        const recipientList = doc.recipients.map((r, i) => ({
          id: r.id,
          name: r.name,
          email: r.email,
          order: r.order ?? i + 1,
          approvalType: r.approvalType ?? ('Signature' as ApprovalType),
          roleLabel: r.roleLabel
        }));
        this.recipients.set(recipientList);

        const validRecipientIds = new Set(recipientList.map(r => r.id).filter((id): id is string => !!id));
        const firstRecipientId = recipientList.sort((a, b) => a.order - b.order)[0]?.id;

        const fields: DesignerField[] = doc.fields.map(f => ({
          tempId: uuidv4(),
          id: f.id,
          fieldType: f.fieldType,
          pageNumber: f.page,
          xPosition: f.x,
          yPosition: f.y,
          width: f.width ?? 200,
          height: f.height ?? 60,
          isRequired: f.isRequired,
          recipientId: (f.recipientId && validRecipientIds.has(f.recipientId)) ? f.recipientId : firstRecipientId,
          zIndex: f.zIndex ?? 1,
          options: f.options,
          groupName: f.groupName,
          color: '#6366f1',
          label: f.label ?? ''
        }));
        this.allFields.set(fields);
        this.pushHistory(fields);
        this.loadPdf(doc.storagePath);

        // Pre-select the first recipient
        if (recipientList.length > 0) {
          this.selectedRecipientId.set(recipientList[0].id ?? null);
        }
      }
    });

    // Load users for recipient picker
    this.usersLoading.set(true);
    this.userService.getUsers({ pageSize: 200 }).subscribe({
      next: result => { this.usersList.set(result.items); this.usersLoading.set(false); },
      error: () => this.usersLoading.set(false)
    });

    document.addEventListener('keydown', this.handleKeydown.bind(this));
  }

  ngAfterViewInit(): void {}

  ngOnDestroy(): void {
    document.removeEventListener('keydown', this.handleKeydown.bind(this));
  }

  openAddRecipient(): void {
    this.recipientMode.set('pick');
    this.userSearch.set('');
    this.recipientForm.reset();
    this.showAddRecipient.set(true);
  }

  addRecipientFromUser(user: UserDto): void {
    this.vacationService.checkAvailability(user.email).subscribe({
      next: avail => {
        if (avail.isOnVacation) {
          const until = avail.availableOn ? new Date(avail.availableOn).toLocaleDateString() : this.translate.instant('designer.notifications.later');
          const msg = this.translate.instant('designer.notifications.onVacation', { email: user.email, until });
          this.snackBar.open(msg, this.translate.instant('designer.notifications.dismiss'), { duration: 6000 });
          return;
        }
        this.commitRecipient(user.fullName, user.email);
      },
      error: () => this.commitRecipient(user.fullName, user.email)
    });
  }

  private async loadPdf(storagePath: string): Promise<void> {
    try {
      const pdfjsLib = await import('pdfjs-dist');
      pdfjsLib.GlobalWorkerOptions.workerSrc = new URL(
        'pdfjs-dist/build/pdf.worker.mjs', import.meta.url
      ).toString();
      const blob = await firstValueFrom(this.documentService.stream(this.documentId));
      const data = await blob.arrayBuffer();
      this.pdfDoc = await pdfjsLib.getDocument({ data }).promise;
      this.pdfLoading.set(false);
      setTimeout(() => this.renderPage(this.currentPage()), 0);
    } catch {
      this.pdfLoading.set(false);
    }
  }

  private async renderPage(pageNum: number): Promise<void> {
    if (!this.pdfDoc || !this.pdfCanvasRef) return;
    const pdfjsLib = await import('pdfjs-dist');
    const pdfDocTyped = this.pdfDoc as Awaited<ReturnType<typeof pdfjsLib.getDocument>['promise']>;
    const page = await pdfDocTyped.getPage(pageNum);
    const viewport = page.getViewport({ scale: 1.5 });
    const canvas = this.pdfCanvasRef.nativeElement;
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    this.canvasWidth = viewport.width;
    this.canvasHeight = viewport.height;
    const ctx = canvas.getContext('2d')!;
    await page.render({ canvasContext: ctx, viewport, canvas }).promise;
  }

  async prevPage(): Promise<void> {
    if (this.currentPage() <= 1) return;
    this.currentPage.update(p => p - 1);
    await this.renderPage(this.currentPage());
  }

  async nextPage(): Promise<void> {
    if (!this.doc() || this.currentPage() >= this.doc()!.pageCount) return;
    this.currentPage.update(p => p + 1);
    await this.renderPage(this.currentPage());
  }

  zoom(delta: number): void {
    this.zoomLevel.update(z => Math.min(3, Math.max(0.25, z + delta)));
  }

  /** Rounds a coordinate to the nearest grid line when snapping is on, else to the nearest pixel. */
  private snap(v: number): number {
    return this.snapToGrid() ? Math.round(v / this.gridSize) * this.gridSize : Math.round(v);
  }

  onDrop(event: CdkDragDrop<DesignerField[]>): void {
    const dragData = event.item.data;
    if (!dragData?.type) return;

    // Require a recipient to be selected when there are recipients configured
    if (this.recipients().length > 0 && !this.selectedRecipientId()) {
      this.snackBar.open(
        this.translate.instant('designer.notifications.selectRecipientFirst'),
        this.translate.instant('designer.notifications.ok'),
        { duration: 4000 }
      );
      return;
    }

    // Fields belong to content signers — reviewers, approvers, and viewers act without fields.
    const activeId = this.selectedRecipientId();
    if (activeId && !this.isSignerRole(activeId)) {
      this.snackBar.open(
        this.translate.instant('designer.notifications.nonSignerNoFields'),
        this.translate.instant('designer.notifications.ok'),
        { duration: 4000 }
      );
      return;
    }

    const wrapper = this.pdfWrapperRef.nativeElement.getBoundingClientRect();
    const dropPoint = event.dropPoint;
    const x = Math.max(0, (dropPoint.x - wrapper.left) / this.zoomLevel());
    const y = Math.max(0, (dropPoint.y - wrapper.top) / this.zoomLevel());

    const recipientId = this.selectedRecipientId() ?? this.recipients()[0]?.id ?? undefined;

    // New radio buttons join the most recently placed radio's group so consecutively dropped
    // options form one single-choice group by default; rename per-field in the properties panel.
    let groupName: string | undefined;
    if (dragData.type === 'Radio') {
      const lastRadio = [...this.allFields()].reverse().find(f => f.fieldType === 'Radio');
      groupName = lastRadio?.groupName ?? 'group-1';
    }

    const newField: DesignerField = {
      tempId: uuidv4(),
      id: undefined,
      fieldType: dragData.type,
      pageNumber: this.currentPage(),
      xPosition: this.snap(x),
      yPosition: this.snap(y),
      width: dragData.width,
      height: dragData.height,
      isRequired: true,
      recipientId,
      zIndex: this.allFields().length + 1,
      color: '#6366f1',
      label: '',
      options: undefined,
      groupName
    };

    const updated = [...this.allFields(), newField];
    this.allFields.set(updated);
    this.pushHistory(updated);
    this.selectField(newField);
  }

  startFieldDrag(event: MouseEvent, field: DesignerField): void {
    if (event.button !== 0 || (event.target as HTMLElement).closest('.resize-handle, .field-delete-btn')) return;
    event.preventDefault();
    this.selectField(field);
    const wrapper = this.pdfWrapperRef.nativeElement;
    const startRect = wrapper.getBoundingClientRect();
    // Grab offset in unscaled field coordinates — the wrapper's client rect is zoom-scaled.
    const grabX = (event.clientX - startRect.left) / this.zoomLevel() - field.xPosition;
    const grabY = (event.clientY - startRect.top) / this.zoomLevel() - field.yPosition;
    let moved = false;
    const onMove = (e: MouseEvent) => {
      if (!moved) moved = Math.abs(e.clientX - event.clientX) > 2 || Math.abs(e.clientY - event.clientY) > 2;
      if (!moved) return;
      // Re-measure each move so the field stays under the cursor even if the canvas area scrolls mid-drag.
      const rect = wrapper.getBoundingClientRect();
      const updated = this.allFields().map(f => f.tempId === field.tempId
        ? { ...f,
            xPosition: Math.min(Math.max(0, this.snap((e.clientX - rect.left) / this.zoomLevel() - grabX)), Math.max(0, this.canvasWidth - f.width)),
            yPosition: Math.min(Math.max(0, this.snap((e.clientY - rect.top) / this.zoomLevel() - grabY)), Math.max(0, this.canvasHeight - f.height)) }
        : f);
      this.allFields.set(updated);
      if (this.selectedField()?.tempId === field.tempId) {
        this.selectedField.set(updated.find(f => f.tempId === field.tempId) ?? null);
      }
    };
    const onUp = () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      if (moved) {
        this.pushHistory(this.allFields());
        // The click that follows a drag can land on the wrapper (field clamped at an edge) — don't let it deselect.
        this.suppressCanvasClick = true;
        setTimeout(() => this.suppressCanvasClick = false);
      }
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }

  onCanvasClick(event: MouseEvent): void {
    if (this.suppressCanvasClick) return;
    if ((event.target as HTMLElement).closest('.placed-field')) return;
    this.selectedField.set(null);
  }

  selectField(field: DesignerField, event?: MouseEvent): void {
    event?.stopPropagation();
    this.selectedField.set(field);
  }

  deleteField(field: DesignerField, event: MouseEvent | Event): void {
    (event as MouseEvent).stopPropagation();
    const updated = this.allFields().filter(f => f.tempId !== field.tempId);
    this.allFields.set(updated);
    if (this.selectedField()?.tempId === field.tempId) this.selectedField.set(null);
    this.pushHistory(updated);
  }

  updateFieldProp(prop: keyof DesignerField, value: unknown): void {
    const sel = this.selectedField();
    if (!sel) return;
    const updated = this.allFields().map(f =>
      f.tempId === sel.tempId ? { ...f, [prop]: value } : f
    );
    this.allFields.set(updated);
    this.selectedField.set(updated.find(f => f.tempId === sel.tempId) ?? null);
    this.pushHistory(updated);
  }

  assignFieldToRecipient(recipientId: string): void {
    this.updateFieldProp('recipientId', recipientId);
  }

  selectRecipient(recipientId: string): void {
    this.selectedRecipientId.update(id => id === recipientId ? null : recipientId);
  }

  addRecipient(): void {
    if (this.recipientForm.invalid) return;
    const { name, email } = this.recipientForm.value;
    this.vacationService.checkAvailability(email!).subscribe({
      next: avail => {
        if (avail.isOnVacation) {
          const until = avail.availableOn ? new Date(avail.availableOn).toLocaleDateString() : this.translate.instant('designer.notifications.later');
          const msg = this.translate.instant('designer.notifications.onVacation', { email, until });
          this.snackBar.open(msg, this.translate.instant('designer.notifications.dismiss'), { duration: 6000 });
          return;
        }
        this.commitRecipient(name!, email!);
      },
      error: () => this.commitRecipient(name!, email!)
    });
  }

  private commitRecipient(name: string, email: string): void {
    const order = this.workflowType() === 'Parallel' ? 1 : this.recipients().length + 1;
    const newRecipient: RecipientInputDto = {
      id: uuidv4(),
      name,
      email,
      order,
      approvalType: 'Signature'
    };
    this.recipients.update(rs => [...rs, newRecipient]);
    if (!this.selectedRecipientId()) {
      this.selectedRecipientId.set(newRecipient.id!);
    }
    this.recipientForm.reset();
    this.showAddRecipient.set(false);
  }

  /** Roles a designer-flow participant can hold. Stamp is intentionally omitted (niche). */
  readonly participantRoles: ApprovalType[] = ['Signature', 'Approval', 'Review', 'ViewOnly'];

  recipientRole(id: string): ApprovalType {
    return this.recipients().find(r => r.id === id)?.approvalType ?? 'Signature';
  }

  /** Content signers get fields; reviewers/approvers/viewers gate (or watch) without fields. */
  isSignerRole(id: string): boolean {
    const role = this.recipientRole(id);
    return role === 'Signature' || role === 'Stamp';
  }

  recipientRoleLabelOf(id: string): string {
    return this.recipients().find(r => r.id === id)?.roleLabel ?? '';
  }

  setRecipientRole(id: string, role: ApprovalType): void {
    this.recipients.update(rs => rs.map(r => r.id === id ? { ...r, approvalType: role } : r));

    if (role !== 'Signature' && role !== 'Stamp' && this.allFields().some(f => f.recipientId === id)) {
      this.allFields.update(fields =>
        fields.map(f => f.recipientId === id ? { ...f, recipientId: undefined } : f));
      this.snackBar.open(
        this.translate.instant('designer.notifications.fieldsUnassignedForRole'),
        this.translate.instant('designer.notifications.ok'),
        { duration: 4000 }
      );
    }
  }

  setRecipientRoleLabel(id: string, label: string): void {
    this.recipients.update(rs => rs.map(r => r.id === id ? { ...r, roleLabel: label.trim() || undefined } : r));
  }

  moveRecipient(index: number, direction: -1 | 1): void {
    const list = [...this.recipients()];
    const target = index + direction;
    if (target < 0 || target >= list.length) return;
    const tmp = list[index];
    list[index] = list[target];
    list[target] = tmp;
    this.recipients.set(list.map((r, i) => ({ ...r, order: i + 1 })));
  }

  /**
   * Same rule as the request wizard and the template designer: from "one after the other" each person starts on their
   * own stage, from "all at once" everyone starts on the same one; back to "one after the other" the stage order
   * becomes the order of the list.
   */
  setWorkflowType(type: WorkflowType): void {
    const previous = this.workflowType();
    if (type === previous) return;
    this.recipients.update(rs => {
      if (type === 'Mixed') return rs.map((r, i) => ({ ...r, order: previous === 'Parallel' ? 1 : i + 1 }));
      if (type === 'Sequential') return [...rs].sort((a, b) => a.order - b.order).map((r, i) => ({ ...r, order: i + 1 }));
      return rs;
    });
    this.workflowType.set(type);
  }

  /** The participants as stage items for the shared lane editor. A computed, so the cards are not rebuilt on every refresh. */
  readonly stageItems = computed(() =>
    this.recipientColors().map((rc, index) => ({ stage: rc.order, rc, index })));

  /** A drop in the lane editor: each participant takes the stage they were dropped on. */
  onStagesChanged(items: { stage: number; index: number }[]): void {
    const stageByIndex = new Map(items.map(i => [i.index, i.stage]));
    this.recipients.update(rs => rs.map((r, i) => ({ ...r, order: stageByIndex.get(i) ?? r.order })));
  }

  changeStage(index: number, delta: -1 | 1): void {
    const r = this.recipients()[index];
    if (!r) return;
    this.recipients.update(rs => rs.map((x, i) => i === index ? { ...x, order: Math.max(1, x.order + delta) } : x));
  }

  removeRecipient(index: number): void {
    const list = [...this.recipients()];
    const removed = list[index];
    list.splice(index, 1);
    this.recipients.set(list.map((r, i) => ({ ...r, order: i + 1 })));

    // Unassign fields that belonged to the removed recipient
    this.allFields.update(fields =>
      fields.map(f => f.recipientId === removed.id ? { ...f, recipientId: undefined } : f)
    );

    // Clear or shift the active selection
    if (this.selectedRecipientId() === removed.id) {
      this.selectedRecipientId.set(list[0]?.id ?? null);
    }
  }

  startResize(event: MouseEvent, field: DesignerField): void {
    event.stopPropagation();
    event.preventDefault();
    const startX = event.clientX;
    const startY = event.clientY;
    const startW = field.width;
    const startH = field.height;

    const onMove = (e: MouseEvent) => {
      const updated = this.allFields().map(f =>
        f.tempId === field.tempId
          ? { ...f, width: Math.max(40, this.snap(startW + (e.clientX - startX) / this.zoomLevel())), height: Math.max(20, this.snap(startH + (e.clientY - startY) / this.zoomLevel())) }
          : f
      );
      this.allFields.set(updated);
      if (this.selectedField()?.tempId === field.tempId) {
        this.selectedField.set(updated.find(f => f.tempId === field.tempId) ?? null);
      }
    };
    const onUp = () => {
      this.pushHistory(this.allFields());
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    };
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }

  /**
   * Asks the server to find the contract's fields and adds the proposals to the canvas, unsaved: they can be moved,
   * relabelled or deleted like any other field, and nothing is stored until Save. A proposal that overlaps a field
   * already placed is skipped, so running it twice, or after placing fields by hand, adds nothing twice.
   */
  detectFields(): void {
    this.detecting.set(true);
    this.documentService.stream(this.documentId).pipe(switchMap(blob => this.pdfTemplateService.detectFields(blob))).subscribe({
      next: result => {
        const existing = this.allFields();
        const recipientId = this.selectedRecipientId() ?? this.recipients()[0]?.id ?? undefined;
        const added: DesignerField[] = [];
        for (const d of result.fields) {
          if (existing.some(f => f.pageNumber === d.page && this.overlapsField(f, d))) continue;
          added.push({
            tempId: uuidv4(),
            id: undefined,
            fieldType: d.type === 'Textarea' ? 'MultilineText' : d.type,
            pageNumber: d.page,
            xPosition: this.snap(d.x),
            yPosition: this.snap(d.y),
            width: d.width,
            height: d.height,
            isRequired: d.isRequired,
            recipientId,
            zIndex: existing.length + added.length + 1,
            color: '#6366f1',
            label: d.label,
            options: d.options.length ? JSON.stringify(d.options) : undefined
          });
        }
        if (added.length) {
          const updated = [...existing, ...added];
          this.allFields.set(updated);
          this.pushHistory(updated);
        }
        this.detecting.set(false);
        // With no vision model installed, an empty result usually means "static PDF": say why rather than "none found".
        const key = added.length ? 'pdfTemplates.designer.detected'
          : result.model === null && result.fields.length === 0 ? 'pdfTemplates.designer.detectNoModel'
          : 'pdfTemplates.designer.detectNone';
        this.snackBar.open(this.translate.instant(key, { count: added.length }), 'OK', { duration: 6000 });
      },
      error: (err: { status?: number }) => {
        this.detecting.set(false);
        const key = err?.status === 503 ? 'pdfTemplates.designer.detectUnavailable' : 'pdfTemplates.designer.detectFailed';
        this.snackBar.open(this.translate.instant(key), 'OK', { duration: 5000 });
      }
    });
  }

  private overlapsField(a: DesignerField, b: { x: number; y: number; width: number; height: number }): boolean {
    const w = Math.min(a.xPosition + a.width, b.x + b.width) - Math.max(a.xPosition, b.x);
    const h = Math.min(a.yPosition + a.height, b.y + b.height) - Math.max(a.yPosition, b.y);
    return w > 0 && h > 0 && (w * h) / Math.min(a.width * a.height, b.width * b.height) > 0.5;
  }

  private pushHistory(fields: DesignerField[]): void {
    const idx = this.historyIndex();
    const trimmed = this.history().slice(0, idx + 1);
    this.history.set([...trimmed, [...fields]]);
    this.historyIndex.set(trimmed.length);
  }

  undo(): void {
    const idx = this.historyIndex();
    if (idx <= 0) return;
    this.historyIndex.set(idx - 1);
    this.allFields.set([...this.history()[idx - 1]]);
    this.selectedField.set(null);
  }

  redo(): void {
    const idx = this.historyIndex();
    if (idx >= this.history().length - 1) return;
    this.historyIndex.set(idx + 1);
    this.allFields.set([...this.history()[idx + 1]]);
  }

  private handleKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && event.key === 'z') { event.preventDefault(); this.undo(); }
    if ((event.ctrlKey || event.metaKey) && (event.key === 'y' || (event.shiftKey && event.key === 'z'))) { event.preventDefault(); this.redo(); }
    if (event.key === 'Delete' && this.selectedField()) {
      this.deleteField(this.selectedField()!, event);
    }
    if (this.selectedField() && ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) {
      event.preventDefault();
      const delta = event.shiftKey ? 10 : 1;
      const dir = { ArrowUp: [0, -delta], ArrowDown: [0, delta], ArrowLeft: [-delta, 0], ArrowRight: [delta, 0] }[event.key]!;
      const sel = this.selectedField()!;
      const updated = this.allFields().map(f =>
        f.tempId === sel.tempId
          ? { ...f, xPosition: f.xPosition + dir[0], yPosition: f.yPosition + dir[1] }
          : f
      );
      this.allFields.set(updated);
      this.selectedField.set(updated.find(f => f.tempId === sel.tempId) ?? null);
    }
  }

  save(): void {
    if (this.recipients().length === 0) {
      this.snackBar.open(
        this.translate.instant('designer.notifications.addRecipientBeforeSaving'),
        this.translate.instant('designer.notifications.dismiss'),
        { duration: 4000 }
      );
      return;
    }

    this.saving.set(true);
    const fields: FieldDto[] = this.allFields().map(f => ({
      id: f.id,
      fieldType: f.fieldType,
      page: f.pageNumber,
      x: f.xPosition,
      y: f.yPosition,
      width: f.width,
      height: f.height,
      isRequired: f.isRequired,
      recipientId: f.recipientId,
      zIndex: f.zIndex,
      options: f.options,
      groupName: f.groupName,
      label: f.label || undefined
    }));

    const wfType = this.workflowType();
    // For Parallel all orders must be equal so the engine treats them as one simultaneous stage
    const recipients: RecipientInputDto[] = wfType === 'Parallel'
      ? this.recipients().map(r => ({ ...r, order: 1 }))
      : this.recipients();

    // Recipients must be persisted before fields — fields carry recipientId FK references.
    this.documentService.saveRecipients(this.documentId, wfType, recipients, this.hideRecipientIdentities()).pipe(
      switchMap(() => this.documentService.saveFields(this.documentId, fields))
    ).subscribe({
      next: () => {
        this.saving.set(false);
        this.snackBar.open(
          this.translate.instant('designer.notifications.savedSuccessfully'),
          this.translate.instant('designer.notifications.dismiss'),
          { duration: 3000 }
        );
      },
      error: (err: { error?: { detail?: string; errors?: Record<string, string[]> } }) => {
        this.saving.set(false);
        const msg = err.error?.errors
          ? Object.values(err.error.errors).flat().join(' ')
          : err.error?.detail ?? this.translate.instant('designer.notifications.saveFailed');
        this.snackBar.open(msg, this.translate.instant('designer.notifications.dismiss'), { duration: 6000 });
      }
    });
  }
}
