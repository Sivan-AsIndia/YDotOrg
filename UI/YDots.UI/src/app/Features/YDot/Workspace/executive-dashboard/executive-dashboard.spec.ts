import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ExecutiveDashboardComponent } from './executive-dashboard';

describe('ExecutiveDashboardComponent', () => {
  let component: ExecutiveDashboardComponent;
  let fixture: ComponentFixture<ExecutiveDashboardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExecutiveDashboardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ExecutiveDashboardComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
