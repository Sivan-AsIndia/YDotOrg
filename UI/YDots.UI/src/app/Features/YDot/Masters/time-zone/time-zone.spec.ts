import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TimeZone } from './time-zone';

describe('TimeZone', () => {
  let component: TimeZone;
  let fixture: ComponentFixture<TimeZone>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TimeZone],
    }).compileComponents();

    fixture = TestBed.createComponent(TimeZone);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
