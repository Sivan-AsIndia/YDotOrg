import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Country } from './country';

describe('Country', () => {
  let component: Country;
  let fixture: ComponentFixture<Country>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Country],
    }).compileComponents();

    fixture = TestBed.createComponent(Country);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
