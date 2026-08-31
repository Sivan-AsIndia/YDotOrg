import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FollowUpQueue } from './follow-up-queue';

describe('FollowUpQueue', () => {
  let component: FollowUpQueue;
  let fixture: ComponentFixture<FollowUpQueue>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FollowUpQueue],
    }).compileComponents();

    fixture = TestBed.createComponent(FollowUpQueue);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
