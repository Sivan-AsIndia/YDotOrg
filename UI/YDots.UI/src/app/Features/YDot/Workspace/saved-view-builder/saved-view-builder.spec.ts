import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SavedViewBuilderComponent } from './saved-view-builder';

describe('SavedViewBuilderComponent', () => {
  let component: SavedViewBuilderComponent;
  let fixture: ComponentFixture<SavedViewBuilderComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SavedViewBuilderComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SavedViewBuilderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
