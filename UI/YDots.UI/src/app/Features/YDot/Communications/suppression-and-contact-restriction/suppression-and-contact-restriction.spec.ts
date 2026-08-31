import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SuppressionAndContactRestriction } from './suppression-and-contact-restriction';

describe('SuppressionAndContactRestriction', () => {
  let component: SuppressionAndContactRestriction;
  let fixture: ComponentFixture<SuppressionAndContactRestriction>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SuppressionAndContactRestriction],
    }).compileComponents();

    fixture = TestBed.createComponent(SuppressionAndContactRestriction);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
