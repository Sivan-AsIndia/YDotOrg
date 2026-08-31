import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OutboundMessageComposer } from './outbound-message-composer';

describe('OutboundMessageComposer', () => {
  let component: OutboundMessageComposer;
  let fixture: ComponentFixture<OutboundMessageComposer>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OutboundMessageComposer],
    }).compileComponents();

    fixture = TestBed.createComponent(OutboundMessageComposer);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
