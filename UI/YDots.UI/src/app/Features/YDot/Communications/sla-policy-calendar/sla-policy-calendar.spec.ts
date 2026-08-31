import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SlaPolicyCalendar } from './sla-policy-calendar';

describe('SlaPolicyCalendar', () => {
  let component: SlaPolicyCalendar;
  let fixture: ComponentFixture<SlaPolicyCalendar>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SlaPolicyCalendar],
    }).compileComponents();

    fixture = TestBed.createComponent(SlaPolicyCalendar);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
