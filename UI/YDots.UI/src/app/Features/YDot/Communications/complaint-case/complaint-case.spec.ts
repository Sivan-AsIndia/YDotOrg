import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ComplaintCase } from './complaint-case';

describe('ComplaintCase', () => {
  let component: ComplaintCase;
  let fixture: ComponentFixture<ComplaintCase>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ComplaintCase],
    }).compileComponents();

    fixture = TestBed.createComponent(ComplaintCase);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
