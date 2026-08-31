import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DonationHistory } from './donation-history';

describe('DonationHistory', () => {
  let component: DonationHistory;
  let fixture: ComponentFixture<DonationHistory>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DonationHistory],
    }).compileComponents();

    fixture = TestBed.createComponent(DonationHistory);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
