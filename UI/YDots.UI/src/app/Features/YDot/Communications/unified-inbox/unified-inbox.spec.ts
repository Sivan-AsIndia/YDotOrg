import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UnifiedInbox } from './unified-inbox';

describe('UnifiedInbox', () => {
  let component: UnifiedInbox;
  let fixture: ComponentFixture<UnifiedInbox>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UnifiedInbox],
    }).compileComponents();

    fixture = TestBed.createComponent(UnifiedInbox);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
