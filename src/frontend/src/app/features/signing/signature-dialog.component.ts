import {
  Component, inject, signal, ViewChild, ElementRef,
  AfterViewInit, OnDestroy, Inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';

interface DialogData { fieldType: string; }

const FONTS = [
  { label: 'Pacifico', style: 'cursive' },
  { label: 'Dancing Script', style: '"Dancing Script", cursive' },
  { label: 'Satisfy', style: '"Satisfy", cursive' },
  { label: 'Print', style: 'monospace' }
];

@Component({
  selector: 'sf-signature-dialog',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatDialogModule, MatButtonModule, MatIconModule,
    MatTabsModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    TranslatePipe
  ],
  template: `
    <h2 mat-dialog-title>{{ (data.fieldType === 'Initials' ? 'signing.signatureDialog.addInitials' : 'signing.signatureDialog.addSignature') | translate }}</h2>

    <mat-dialog-content class="dialog-content">
      <mat-tab-group [selectedIndex]="activeTab()" (selectedIndexChange)="activeTab.set($event)">
        <!-- Draw Tab -->
        <mat-tab [label]="'signing.signatureDialog.drawTab' | translate">
          <div class="tab-content">
            <p class="tab-hint">{{ 'signing.signatureDialog.drawHint' | translate }}</p>
            <div class="canvas-wrapper">
              <canvas #signatureCanvas class="signature-canvas"
                      (mousedown)="startDraw($event)" (mousemove)="draw($event)"
                      (mouseup)="stopDraw()" (mouseleave)="stopDraw()"
                      (touchstart)="startDrawTouch($event)" (touchmove)="drawTouch($event)"
                      (touchend)="stopDraw()">
              </canvas>
            </div>
            <div class="canvas-actions">
              <button mat-button (click)="clearCanvas()"><mat-icon>refresh</mat-icon> {{ 'signing.signatureDialog.clear' | translate }}</button>
              <span class="canvas-hint">{{ 'signing.signatureDialog.drawYourField' | translate: { fieldType: data.fieldType.toLowerCase() } }}</span>
            </div>
          </div>
        </mat-tab>

        <!-- Type Tab -->
        <mat-tab [label]="'signing.signatureDialog.typeTab' | translate">
          <div class="tab-content">
            <p class="tab-hint">{{ 'signing.signatureDialog.typeHint' | translate }}</p>
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ (data.fieldType === 'Initials' ? 'signing.signatureDialog.labelInitials' : 'signing.signatureDialog.labelFullName') | translate }}</mat-label>
              <input matInput [formControl]="typedName" (input)="renderTypedSignature()" />
            </mat-form-field>

            <div class="font-options">
              @for (font of fonts; track font.label; let i = $index) {
                <div class="font-option" [class.selected-font]="selectedFont() === i"
                     (click)="selectedFont.set(i); renderTypedSignature()"
                     [style.font-family]="font.style">
                  {{ typedName.value || ('signing.signatureDialog.yourNamePlaceholder' | translate) }}
                </div>
              }
            </div>

            <canvas #typedCanvas class="typed-preview-canvas" [style.display]="typedName.value ? 'block' : 'none'"></canvas>
          </div>
        </mat-tab>

        <!-- Upload Tab -->
        <mat-tab [label]="'signing.signatureDialog.uploadTab' | translate">
          <div class="tab-content">
            <p class="tab-hint">{{ 'signing.signatureDialog.uploadHint' | translate }}</p>
            @if (!uploadedDataUrl()) {
              <div class="upload-zone" (click)="fileInput.click()" (dragover)="$event.preventDefault()" (drop)="onImageDrop($event)">
                <mat-icon>cloud_upload</mat-icon>
                <p>{{ 'signing.signatureDialog.dropZone' | translate }}</p>
                <input #fileInput type="file" accept="image/*" hidden (change)="onImageSelected($event)" />
              </div>
            } @else {
              <div class="upload-preview">
                <img [src]="uploadedDataUrl()!" [attr.alt]="'signing.signatureDialog.uploadedAlt' | translate" class="sig-preview" />
                <button mat-icon-button (click)="uploadedDataUrl.set(null)"><mat-icon>close</mat-icon></button>
              </div>
            }
          </div>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="primary" (click)="confirm()" [disabled]="!canConfirm()">
        {{ 'signing.signatureDialog.apply' | translate: { fieldType: data.fieldType } }}
      </button>
    </mat-dialog-actions>
  `,
  styleUrl: './signature-dialog.component.scss'
})
export class SignatureDialogComponent implements AfterViewInit, OnDestroy {
  @ViewChild('signatureCanvas') signatureCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('typedCanvas') typedCanvas!: ElementRef<HTMLCanvasElement>;

  readonly dialogRef = inject(MatDialogRef<SignatureDialogComponent>);
  readonly data: DialogData = inject(MAT_DIALOG_DATA);

  readonly activeTab = signal(0);
  readonly selectedFont = signal(0);
  readonly uploadedDataUrl = signal<string | null>(null);
  readonly typedName = new FormControl('');

  readonly fonts = FONTS;

  private isDrawing = false;
  private lastX = 0;
  private lastY = 0;
  private hasDrawn = false;

  ngAfterViewInit(): void {
    this.initCanvas();
  }

  ngOnDestroy(): void {}

  private initCanvas(): void {
    if (!this.signatureCanvas) return;
    const canvas = this.signatureCanvas.nativeElement;
    canvas.width = canvas.offsetWidth;
    canvas.height = 160;
    const ctx = canvas.getContext('2d')!;
    ctx.strokeStyle = '#1e293b';
    ctx.lineWidth = 2.5;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
  }

  startDraw(event: MouseEvent): void {
    this.isDrawing = true;
    const rect = this.signatureCanvas.nativeElement.getBoundingClientRect();
    this.lastX = event.clientX - rect.left;
    this.lastY = event.clientY - rect.top;
  }

  draw(event: MouseEvent): void {
    if (!this.isDrawing) return;
    const canvas = this.signatureCanvas.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    const ctx = canvas.getContext('2d')!;
    ctx.beginPath();
    ctx.moveTo(this.lastX, this.lastY);
    ctx.lineTo(x, y);
    ctx.stroke();
    this.lastX = x; this.lastY = y;
    this.hasDrawn = true;
  }

  startDrawTouch(event: TouchEvent): void {
    event.preventDefault();
    const touch = event.touches[0];
    const rect = this.signatureCanvas.nativeElement.getBoundingClientRect();
    this.isDrawing = true;
    this.lastX = touch.clientX - rect.left;
    this.lastY = touch.clientY - rect.top;
  }

  drawTouch(event: TouchEvent): void {
    event.preventDefault();
    if (!this.isDrawing) return;
    const touch = event.touches[0];
    const canvas = this.signatureCanvas.nativeElement;
    const rect = canvas.getBoundingClientRect();
    const x = touch.clientX - rect.left;
    const y = touch.clientY - rect.top;
    const ctx = canvas.getContext('2d')!;
    ctx.beginPath();
    ctx.moveTo(this.lastX, this.lastY);
    ctx.lineTo(x, y);
    ctx.stroke();
    this.lastX = x; this.lastY = y;
    this.hasDrawn = true;
  }

  stopDraw(): void { this.isDrawing = false; }

  clearCanvas(): void {
    const canvas = this.signatureCanvas.nativeElement;
    canvas.getContext('2d')!.clearRect(0, 0, canvas.width, canvas.height);
    this.hasDrawn = false;
  }

  renderTypedSignature(): void {
    const name = this.typedName.value?.trim();
    if (!name || !this.typedCanvas) return;
    const canvas = this.typedCanvas.nativeElement;
    canvas.width = 480;
    canvas.height = 100;
    const ctx = canvas.getContext('2d')!;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.font = `52px ${FONTS[this.selectedFont()].style}`;
    ctx.fillStyle = '#1e293b';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(name, canvas.width / 2, canvas.height / 2);
  }

  onImageSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.readImageFile(file);
  }

  onImageDrop(event: DragEvent): void {
    event.preventDefault();
    const file = event.dataTransfer?.files[0];
    if (file && file.type.startsWith('image/')) this.readImageFile(file);
  }

  private readImageFile(file: File): void {
    const reader = new FileReader();
    reader.onload = e => this.uploadedDataUrl.set(e.target?.result as string);
    reader.readAsDataURL(file);
  }

  canConfirm(): boolean {
    switch (this.activeTab()) {
      case 0: return this.hasDrawn;
      case 1: return !!(this.typedName.value?.trim());
      case 2: return !!this.uploadedDataUrl();
      default: return false;
    }
  }

  confirm(): void {
    let dataUrl = '';
    let type = '';
    switch (this.activeTab()) {
      case 0:
        dataUrl = this.signatureCanvas.nativeElement.toDataURL('image/png');
        type = 'drawn';
        break;
      case 1:
        dataUrl = this.typedCanvas.nativeElement.toDataURL('image/png');
        type = 'typed';
        break;
      case 2:
        dataUrl = this.uploadedDataUrl()!;
        type = 'uploaded';
        break;
    }
    this.dialogRef.close({ dataUrl, type });
  }
}
