import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CommunicationTimeline } from './communication-timeline';

describe('CommunicationTimeline', () => {
  let component: CommunicationTimeline;
  let fixture: ComponentFixture<CommunicationTimeline>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CommunicationTimeline],
    }).compileComponents();

    fixture = TestBed.createComponent(CommunicationTimeline);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
