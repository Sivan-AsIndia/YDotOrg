import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Applayout } from './applayout';

describe('Applayout', () => {
  let component: Applayout;
  let fixture: ComponentFixture<Applayout>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Applayout],
    }).compileComponents();

    fixture = TestBed.createComponent(Applayout);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
