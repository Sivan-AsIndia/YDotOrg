import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FollowUpExecution } from './follow-up-execution';

describe('FollowUpExecution', () => {
  let component: FollowUpExecution;
  let fixture: ComponentFixture<FollowUpExecution>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FollowUpExecution],
    }).compileComponents();

    fixture = TestBed.createComponent(FollowUpExecution);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
