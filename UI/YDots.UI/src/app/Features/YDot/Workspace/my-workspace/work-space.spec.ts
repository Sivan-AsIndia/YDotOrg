import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WorkSpaceComponent } from './work-space';

describe('WorkSpaceComponent', () => {
  let component: WorkSpaceComponent;
  let fixture: ComponentFixture<WorkSpaceComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WorkSpaceComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(WorkSpaceComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
