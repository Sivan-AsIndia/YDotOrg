import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CommunicationExceptionQueue } from './communication-exception-queue';

describe('CommunicationExceptionQueue', () => {
  let component: CommunicationExceptionQueue;
  let fixture: ComponentFixture<CommunicationExceptionQueue>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CommunicationExceptionQueue],
    }).compileComponents();

    fixture = TestBed.createComponent(CommunicationExceptionQueue);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
